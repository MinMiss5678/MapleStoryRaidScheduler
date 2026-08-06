using Application.DTOs;
using Dapper;
using Domain.Entities;
using Infrastructure.Query;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// leader-led Phase 1b 開隊寫路徑：TeamLeaderService.CreateTeamAsync 對真 Postgres 建 leader 隊 + 條件。
/// 驗新欄（Source=leader / PeriodId / LeaderDiscordId / Description）真的寫入，且條件列+職業一起落。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class TeamLeaderServiceIntegrationTests
{
    private readonly PostgresFixture _fx;
    public TeamLeaderServiceIntegrationTests(PostgresFixture fx) => _fx = fx;

    private TeamLeaderService CreateService()
    {
        var db = _fx.CreateDbContext();
        return new TeamLeaderService(
            new BossRepository(db),
            new PeriodQuery(db),
            new TeamSlotRepository(db),
            new TeamSlotRequirementRepository(db));
    }

    [Fact]
    public async Task CreateTeamAsync_PersistsLeaderTeamWithRequirements()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs, requireMembers: 6);
        var periodId = await Seed.PeriodAsync(cs,
            new DateTimeOffset(2026, 4, 7, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 13, 23, 59, 59, TimeSpan.Zero));
        await Seed.PlayerAsync(cs, 999, "隊長");

        var slot = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var teamSlotId = await CreateService().CreateTeamAsync(new CreateTeamCommand
        {
            LeaderDiscordId = 999,
            BossId = bossId,
            SlotDateTime = slot,
            Description = "楓葉祝福9",
            Requirements =
            [
                new CreateTeamRequirementDto
                {
                    Count = 1, MinClearCount = 1,
                    Jobs =
                    [
                        new CreateTeamRequirementJobDto { Job = "箭神", MinAttackPower = 900 },
                        new CreateTeamRequirementJobDto { Job = "槍神", MinAttackPower = 1000 },
                    ]
                }
            ]
        });

        Assert.True(teamSlotId > 0);

        // TeamSlot 新欄實際寫入（read 路徑尚未映射這些欄，故直接查 DB 驗）
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var row = await conn.QuerySingleAsync<(string Source, int? PeriodId, long? LeaderDiscordId, string? Description)>(
            """SELECT "Source", "PeriodId", "LeaderDiscordId", "Description" FROM "TeamSlot" WHERE "Id" = @id;""",
            new { id = teamSlotId });
        Assert.Equal(TeamSlotSource.Leader, row.Source);
        Assert.Equal(periodId, row.PeriodId);
        Assert.Equal(999L, row.LeaderDiscordId);
        Assert.Equal("楓葉祝福9", row.Description);

        // 條件列 + 職業一起落
        var reqs = (await new TeamSlotRequirementRepository(_fx.CreateDbContext())
            .GetByTeamSlotIdAsync(teamSlotId)).ToList();
        Assert.Single(reqs);
        Assert.Equal(2, reqs[0].Jobs.Count);
        Assert.Contains(reqs[0].Jobs, j => j.Job == "箭神" && j.MinAttackPower == 900);
    }
}
