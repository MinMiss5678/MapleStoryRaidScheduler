using Application.DTOs;
using Dapper;
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
            new RegistrationLock(dbContext));
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
}
