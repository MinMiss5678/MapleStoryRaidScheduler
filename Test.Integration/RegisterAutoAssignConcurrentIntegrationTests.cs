using Application.DTOs;
using Dapper;
using Domain.Helpers;
using Infrastructure.Query;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 真正併發（Task.WhenAll，非序列）驗證 AcquireAutoAssignLockAsync 真的擋住「兩人同時報名各開一隊」的
/// read-then-write race（見 TeamSlotAutoAssignService.AutoAssignAsync 註解的設計意圖）。
/// RegistrationLockIntegrationTests 只用 pg_try_advisory_xact_lock 決定性地驗鎖本身互斥，
/// 沒驗「鎖 + 讀快照 + 配對到現有隊伍/開新隊」這整套組合在真並發下有沒有真的守住不變式——這裡才是。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class RegisterAutoAssignConcurrentIntegrationTests
{
    private readonly PostgresFixture _fx;
    public RegisterAutoAssignConcurrentIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task ConcurrentRegister_SameBossSameAvailability_MergesIntoOneTeam_NotTwo()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs, requireMembers: 6);

        // 週期從下一個重製日（週二）起算，確保報名截止日永遠在「現在」之後，不會撞到 EnsureRegistrationOpen 的檢查
        var periodStart = SlotDateCalculator.NextReset(DateTimeOffset.UtcNow.AddDays(10));
        var periodEnd = periodStart.AddDays(7).AddSeconds(-1);
        var periodId = await Seed.PeriodAsync(cs, periodStart, periodEnd);

        await Seed.PlayerAsync(cs, 111, "P1");
        await Seed.CharacterAsync(cs, "char1", 111, "C1", "Hero", 1000);
        await Seed.PlayerAsync(cs, 222, "P2");
        await Seed.CharacterAsync(cs, "char2", 222, "C2", "Bishop", 900);

        // 兩人時間可用性完全相同（重製日 20:00-00:00）→ 沒有 race 的話應該配到同一隊
        var availabilities = new List<PlayerAvailabilityDto>
        {
            new PlayerAvailabilityDto { Weekday = 2, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(0, 0) }
        };

        RegisterCreateCommand BuildCommand(ulong discordId, string characterId) => new()
        {
            DiscordId = discordId,
            PeriodId = periodId,
            Availabilities = availabilities,
            CharacterRegisters = new List<CharacterRegisterDto>
            {
                new CharacterRegisterDto { CharacterId = characterId, BossId = bossId, Rounds = 1 }
            }
        };

        async Task<Exception?> TryRegister(RegisterCreateCommand command)
        {
            var dbContext = _fx.CreateDbContext();
            await dbContext.BeginAsync();
            try
            {
                var periodQuery = new PeriodQuery(dbContext);
                var teamSlotRepository = new TeamSlotRepository(dbContext);
                var teamSlotCharacterRepository = new TeamSlotCharacterRepository(dbContext);
                var bossRepository = new BossRepository(dbContext);
                var playerAvailabilityRepository = new PlayerAvailabilityRepository(dbContext);
                var mergeService = new TeamSlotMergeService(
                    teamSlotRepository, teamSlotCharacterRepository, periodQuery,
                    bossRepository, playerAvailabilityRepository, new JobCategoryRepository(dbContext));
                var autoAssignService = new TeamSlotAutoAssignService(
                    teamSlotRepository, teamSlotCharacterRepository, periodQuery,
                    new CharacterQuery(dbContext, periodQuery), bossRepository,
                    new PlayerRepository(dbContext), mergeService, new RegistrationLock(dbContext));

                var registerService = new RegisterService(
                    periodQuery,
                    new PlayerRegisterRepository(dbContext),
                    new CharacterRegisterRepository(dbContext),
                    playerAvailabilityRepository,
                    teamSlotCharacterRepository,
                    autoAssignService,
                    new SystemConfigService(dbContext, new Outbox(dbContext)));

                await registerService.CreateAsync(command);
                await dbContext.CommitAsync();
                return null;
            }
            catch (Exception ex)
            {
                await dbContext.RollbackAsync();
                return ex;
            }
        }

        // 真正同時發起（不是先做完一個再做下一個）
        var task1 = TryRegister(BuildCommand(111, "char1"));
        var task2 = TryRegister(BuildCommand(222, "char2"));
        var results = await Task.WhenAll(task1, task2);

        Assert.All(results, r => Assert.Null(r)); // 兩邊都該乾淨成功，不該因為鎖而拋例外

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var teamSlotCount = await conn.ExecuteScalarAsync<int>(
            """SELECT COUNT(*) FROM "TeamSlot" WHERE "BossId" = @bossId""", new { bossId });
        Assert.Equal(1, teamSlotCount); // 核心斷言：鎖擋住了 race，兩人併入同一隊，不是各自開了一隊

        var memberCount = await conn.ExecuteScalarAsync<int>(
            """SELECT COUNT(*) FROM "TeamSlotCharacter" WHERE "CharacterId" IN ('char1','char2')""");
        Assert.Equal(2, memberCount);
    }
}
