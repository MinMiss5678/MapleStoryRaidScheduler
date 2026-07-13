using Infrastructure.Query;
using Xunit;

namespace Test.Integration;

[Collection("pg")]
[Trait("Category", "Integration")]
public class PlayerRegisterQueryIntegrationTests
{
    private readonly PostgresFixture _fx;
    public PlayerRegisterQueryIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task GetByNowPeriodIdAsync_JoinsChain_AndGroupsAvailabilities()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;

        // GetByNowAsync 回最新 period → 只需一個 period
        var periodId = await Seed.PeriodAsync(cs,
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(6));
        var bossId = await Seed.BossAsync(cs);
        await Seed.PlayerAsync(cs, 111, "P1");
        await Seed.CharacterAsync(cs, "c1", 111, "Hero", "Warrior", 1000);
        var prId = await Seed.PlayerRegisterAsync(cs, 111, periodId);
        await Seed.CharacterRegisterAsync(cs, prId, "c1", bossId, rounds: 2);
        // 兩個可用時段 → 大 JOIN 會產生兩列，GroupBy 應把它們併回同一角色
        await Seed.AvailabilityAsync(cs, prId, 4, new TimeOnly(20, 0), new TimeOnly(22, 0));
        await Seed.AvailabilityAsync(cs, prId, 5, new TimeOnly(20, 0), new TimeOnly(22, 0));

        var query = new PlayerRegisterQuery(new PeriodQuery(_fx.CreateDbContext()), _fx.CreateDbContext());

        var result = (await query.GetByNowPeriodIdAsync(bossId)).ToList();

        // 一個角色（不因兩個時段而重複），欄位正確、時段被 group 成 2
        Assert.Single(result);
        Assert.Equal("c1", result[0].CharacterId);
        Assert.Equal("Warrior", result[0].Job);
        Assert.Equal(2, result[0].Rounds);
        Assert.Equal(2, result[0].Availabilities.Count);
    }

    [Fact]
    public async Task GetByNowPeriodIdAsync_FiltersByBoss()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var periodId = await Seed.PeriodAsync(cs,
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(6));
        var bossA = await Seed.BossAsync(cs, "A");
        var bossB = await Seed.BossAsync(cs, "B");
        await Seed.PlayerAsync(cs, 111, "P1");
        await Seed.CharacterAsync(cs, "c1", 111, "Hero", "Warrior", 1000);
        var prId = await Seed.PlayerRegisterAsync(cs, 111, periodId);
        await Seed.CharacterRegisterAsync(cs, prId, "c1", bossB, rounds: 1); // 只報 bossB

        var query = new PlayerRegisterQuery(new PeriodQuery(_fx.CreateDbContext()), _fx.CreateDbContext());

        Assert.Empty(await query.GetByNowPeriodIdAsync(bossA)); // 查 bossA → 撈不到
        Assert.Single(await query.GetByNowPeriodIdAsync(bossB)); // 查 bossB → 撈到
    }
}
