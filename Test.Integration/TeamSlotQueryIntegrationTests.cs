using Domain.Entities;
using Infrastructure.Query;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 驗 TeamSlotQuery.GetByPeriodAndBossIdAsync 的多 LEFT JOIN（TeamSlot→TeamSlotCharacter→Boss）
/// + period 範圍過濾（timestamptz）+ bossId 過濾 + BossName 由 Boss JOIN 帶出。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class TeamSlotQueryIntegrationTests
{
    private readonly PostgresFixture _fx;
    public TeamSlotQueryIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task GetByPeriodAndBossIdAsync_JoinBossName_且濾期外與別王()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs, "DEMO王");
        var otherBoss = await Seed.BossAsync(cs, "別的王");
        var start = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 4, 8, 0, 0, 0, TimeSpan.Zero);
        var periodId = await Seed.PeriodAsync(cs, start, end);

        var inPeriod = new DateTimeOffset(2026, 4, 3, 12, 0, 0, TimeSpan.Zero);
        var outPeriod = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        // 期內 + 該王的隊（含 1 成員）→ 應回
        var tsIn = await Seed.TeamSlotAsync(cs, bossId, "auto", inPeriod);
        await Seed.OccupiedSlotAsync(cs, tsIn, 111, "occ1");
        // 期外的隊 → 應被 period 過濾掉
        var tsOut = await Seed.TeamSlotAsync(cs, bossId, "auto", outPeriod);
        await Seed.OccupiedSlotAsync(cs, tsOut, 222, "occ2");
        // 別王、期內的隊 → 應被 bossId 過濾掉
        var tsOther = await Seed.TeamSlotAsync(cs, otherBoss, "auto", inPeriod);
        await Seed.OccupiedSlotAsync(cs, tsOther, 333, "occ3");

        var ctx = _fx.CreateDbContext();
        var query = new TeamSlotQuery(ctx);
        var period = new Period { Id = periodId, StartDate = start, EndDate = end };

        var rows = (await query.GetByPeriodAndBossIdAsync(period, bossId)).ToList();

        // foil：只回期內 + 該 boss 的隊（期外 tsOut、別王 tsOther 都被濾）
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(tsIn, r.TeamSlotId));
        // BossName 由 Boss LEFT JOIN 帶出、成員 occ1 由 TeamSlotCharacter JOIN 帶出
        Assert.Contains(rows, r => r.BossName == "DEMO王" && r.CharacterId == "occ1");
    }
}
