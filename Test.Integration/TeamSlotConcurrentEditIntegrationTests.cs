using Application.DTOs;
using Dapper;
using Domain.Exceptions;
using Infrastructure.Query;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 端到端釘住這個驗收項：移除自動隊最後一人會連帶砍團（真實 SQL 副作用），
/// 若此時有人接著要對同一隊新增成員，應落到「隊伍消失」統一衝突回報，
/// 而不是撞 TeamSlotCharacter.TeamSlotId 的外鍵違反（23503）。
/// 鎖（Phase A）+ 統一衝突回報（Phase B）各自的單元測試都只驗了機制本身，
/// 從沒有串起來跑過這個真實情境——這裡才是。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class TeamSlotConcurrentEditIntegrationTests
{
    private readonly PostgresFixture _fx;
    public TeamSlotConcurrentEditIntegrationTests(PostgresFixture fx) => _fx = fx;

    private TeamSlotService CreateService()
    {
        var dbContext = _fx.CreateDbContext();
        return new TeamSlotService(
            new TeamSlotRepository(dbContext),
            new TeamSlotQuery(dbContext),
            new TeamSlotCharacterRepository(dbContext),
            new PeriodQuery(dbContext),
            new BossRepository(dbContext),
            new RegistrationLock(dbContext),
            new CharacterQuery(dbContext, new PeriodQuery(dbContext)));
    }

    [Fact]
    public async Task RemovingLastMember_CascadesTeamDelete_ThenConcurrentAdd_ReportsConflict_NotFkViolation()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs, requireMembers: 6);
        await Seed.PeriodAsync(cs,
            new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 8, 0, 0, 0, TimeSpan.Zero));
        var teamSlotId = await Seed.TeamSlotAsync(cs, bossId, "auto");
        var lastMemberId = await Seed.OccupiedSlotAsync(cs, teamSlotId, discordId: 111, charId: "occ1");

        // 步驟一：移除最後一個真實成員（走真正的 TeamSlotService.UpdateAsync）
        var service = CreateService();
        var removeRequest = new TeamSlotUpdateRequest
        {
            DeleteTeamSlotIds = new List<int>(),
            TeamSlots = new List<TeamSlotUpdateCommand>
            {
                new TeamSlotUpdateCommand
                {
                    Id = teamSlotId,
                    DeleteTeamSlotCharacterIds = new List<int> { lastMemberId },
                    Characters = new List<TeamSlotMemberDto>()
                }
            }
        };
        var removeResult = await service.UpdateAsync(removeRequest, isAdmin: true, currentDiscordId: 0);
        Assert.Empty(removeResult.ConflictedTeamSlotIds); // 這一步本身應該乾淨成功

        // 驗證：真實副作用發生了——整個 TeamSlot 被連帶砍掉（不是只刪成員列）
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var teamStillExists = await conn.ExecuteScalarAsync<bool>(
            """SELECT EXISTS(SELECT 1 FROM "TeamSlot" WHERE "Id" = @id)""", new { id = teamSlotId });
        Assert.False(teamStillExists);

        // 步驟二：另一個「併發」請求（用同一把鎖序列化後，這個請求重新讀到的就是隊伍已消失）
        // 對同一個（已經不存在的）teamSlotId 嘗試新增成員
        await Seed.PlayerAsync(cs, 222, "P2");
        await Seed.CharacterAsync(cs, "occ2", 222, "C2", "Bishop", 800);
        var addRequest = new TeamSlotUpdateRequest
        {
            DeleteTeamSlotIds = new List<int>(),
            TeamSlots = new List<TeamSlotUpdateCommand>
            {
                new TeamSlotUpdateCommand
                {
                    Id = teamSlotId,
                    DeleteTeamSlotCharacterIds = new List<int>(),
                    Characters = new List<TeamSlotMemberDto>
                    {
                        new TeamSlotMemberDto { Id = null, DiscordId = 222, CharacterId = "occ2" }
                    }
                }
            }
        };

        // 核心斷言：不拋出外鍵違反例外，而是落到統一衝突回報
        var addResult = await service.UpdateAsync(addRequest, isAdmin: true, currentDiscordId: 0);
        Assert.Contains(teamSlotId, addResult.ConflictedTeamSlotIds);
    }

    /// <summary>
    /// 真正併發（Task.WhenAll，非序列）驗證悲觀鎖擋住容量競爭。
    /// AddMember 的 HasRoom 檢查只對「呼叫當下讀到的快照」有效——兩個獨立連線在
    /// READ COMMITTED 下互相看不到對方未提交的 INSERT，各自快照都顯示「還有空位」，
    /// 沒有鎖的話兩邊都會通過檢查、一起寫入造成超編。這裡用只剩 1 個空位的隊伍，
    /// 兩個獨立 TeamSlotService（各自獨立連線）同時搶著新增成員，驗證：
    /// 鎖序列化後，只有一個成功，另一個正確地在「當下真相」下被 AddMember 擋下
    /// （不是意外都成功、也不是意外都失敗），資料庫最終人數不超過容量。
    /// </summary>
    [Fact]
    public async Task ConcurrentAdd_ToTeamWithOneSlotLeft_NeverExceedsCapacity()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs, requireMembers: 2);
        await Seed.PeriodAsync(cs,
            new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 8, 0, 0, 0, TimeSpan.Zero));
        var teamSlotId = await Seed.TeamSlotAsync(cs, bossId, "auto");
        await Seed.OccupiedSlotAsync(cs, teamSlotId, discordId: 111, charId: "occ1"); // 1/2 已滿，剩最後 1 位

        await Seed.PlayerAsync(cs, 222, "P2");
        await Seed.CharacterAsync(cs, "occ2", 222, "C2", "Bishop", 800);
        await Seed.PlayerAsync(cs, 333, "P3");
        await Seed.CharacterAsync(cs, "occ3", 333, "C3", "Warrior", 700);

        TeamSlotUpdateRequest BuildAddRequest(string characterId, ulong discordId) => new()
        {
            DeleteTeamSlotIds = new List<int>(),
            TeamSlots = new List<TeamSlotUpdateCommand>
            {
                new TeamSlotUpdateCommand
                {
                    Id = teamSlotId,
                    DeleteTeamSlotCharacterIds = new List<int>(),
                    Characters = new List<TeamSlotMemberDto>
                    {
                        new TeamSlotMemberDto { Id = null, DiscordId = discordId, CharacterId = characterId }
                    }
                }
            }
        };

        // pg_advisory_xact_lock 是交易級鎖：正式環境靠 UnitOfWorkMiddleware 在 controller 執行前
        // BeginAsync、之後 CommitAsync/RollbackAsync，讓鎖跟整個 request 同一個交易。
        // 這裡手動重現同樣的生命週期，不然鎖沒有交易可以綁、形同虛設。
        async Task<Exception?> TryAdd(string characterId, ulong discordId)
        {
            var dbContext = _fx.CreateDbContext();
            await dbContext.BeginAsync();
            try
            {
                var service = new TeamSlotService(
                    new TeamSlotRepository(dbContext),
                    new TeamSlotQuery(dbContext),
                    new TeamSlotCharacterRepository(dbContext),
                    new PeriodQuery(dbContext),
                    new BossRepository(dbContext),
                    new RegistrationLock(dbContext),
                    new CharacterQuery(dbContext, new PeriodQuery(dbContext)));
                await service.UpdateAsync(BuildAddRequest(characterId, discordId), isAdmin: true, currentDiscordId: 0);
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
        var task1 = TryAdd("occ2", 222);
        var task2 = TryAdd("occ3", 333);
        var results = await Task.WhenAll(task1, task2);

        // 恰好一個成功（null）、一個被 AddMember 正確擋下（DomainException）——不是兩個都成功、也不是兩個都失敗
        Assert.Single(results, r => r == null);
        Assert.Single(results, r => r is DomainException);

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var finalCount = await conn.ExecuteScalarAsync<int>(
            """SELECT COUNT(*) FROM "TeamSlotCharacter" WHERE "TeamSlotId" = @id AND "CharacterId" IS NOT NULL""",
            new { id = teamSlotId });
        Assert.Equal(2, finalCount); // 容量 2，無論如何都不能超過
    }

}
