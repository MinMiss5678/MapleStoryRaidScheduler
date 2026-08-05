using Infrastructure.Query;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 驗 CharacterQuery.GetWithDiscordNameAsync 的複雜 SQL：CTE 聚合當期 Rounds（SUM）、bossId 過濾、
/// ARRAY_AGG 出 RegisteredPeriodIds、Player JOIN 出 DiscordName。這些 mock 測不到，只有真 Postgres 會爆。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class CharacterQueryIntegrationTests
{
    private readonly PostgresFixture _fx;
    public CharacterQueryIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task GetWithDiscordNameAsync_CTE聚合當期場數_且bossId過濾()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var boss1 = await Seed.BossAsync(cs, "王1");
        var boss2 = await Seed.BossAsync(cs, "王2");
        // 只建一個 period → GetActivePeriodIdAsync（最新 StartDate）回它，CTE 就以它為當期
        var periodId = await Seed.PeriodAsync(cs,
            new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 8, 0, 0, 0, TimeSpan.Zero));
        await Seed.PlayerAsync(cs, 1002, "P1");
        await Seed.CharacterAsync(cs, "c1002", 1002, "C1", "Hero", 990);
        // 同角色當期報兩隻王：3 + 2 場
        var reg = await Seed.PlayerRegisterAsync(cs, 1002, periodId);
        await Seed.CharacterRegisterAsync(cs, reg, "c1002", boss1, 3);
        await Seed.CharacterRegisterAsync(cs, reg, "c1002", boss2, 2);

        var ctx = _fx.CreateDbContext();
        var query = new CharacterQuery(ctx, new PeriodQuery(ctx));

        // 無 bossId：CTE SUM 當期所有場數 = 3 + 2 = 5
        var all = (await query.GetWithDiscordNameAsync(1002)).ToList();
        Assert.Single(all);
        var c = all[0];
        Assert.Equal("c1002", c.Id);
        Assert.Equal("P1", c.DiscordName);              // Player LEFT JOIN
        Assert.Equal(5, c.Rounds);                       // CTE SUM 聚合
        Assert.Contains(periodId, c.RegisteredPeriodIds); // ARRAY_AGG DISTINCT

        // 帶 bossId：CTE 只算該王 → boss1 的 3 場
        var forBoss1 = (await query.GetWithDiscordNameAsync(1002, boss1)).ToList();
        Assert.Equal(3, forBoss1[0].Rounds);
    }
}
