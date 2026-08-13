using Infrastructure.Query;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 驗 CharacterQuery.GetWithDiscordNameAsync 的 SQL（period-less 4d：報名/週期退場後只剩
/// Character + Player JOIN 出 DiscordName；Rounds/RegisteredPeriodIds 留預設）。真 Postgres 驗欄位對映。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class CharacterQueryIntegrationTests
{
    private readonly PostgresFixture _fx;
    public CharacterQueryIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task GetWithDiscordNameAsync_ReturnsOwnCharacters_WithDiscordName()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        await Seed.PlayerAsync(cs, 1002, "P1");
        await Seed.CharacterAsync(cs, "c1002", 1002, "C1", "英雄", 990);
        await Seed.CharacterAsync(cs, "c1003", 1002, "C2", "主教", 800);
        // 別的玩家角色（不該回）
        await Seed.PlayerAsync(cs, 2002, "P2");
        await Seed.CharacterAsync(cs, "c2002", 2002, "CX", "夜使者", 700);

        var query = new CharacterQuery(_fx.CreateDbContext());

        var result = (await query.GetWithDiscordNameAsync(1002)).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, c => Assert.Equal("P1", c.DiscordName)); // Player LEFT JOIN
        Assert.All(result, c => Assert.Equal(0, c.Rounds));         // period-less：無當期場數
        Assert.Contains(result, c => c.Id == "c1002" && c.Job == "英雄" && c.AttackPower == 990);
        Assert.Contains(result, c => c.Id == "c1003" && c.Job == "主教");
    }
}
