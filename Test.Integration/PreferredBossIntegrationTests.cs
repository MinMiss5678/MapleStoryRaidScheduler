using Infrastructure.Query;
using Infrastructure.Repositories;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 偏好王資料層整合測試（真 Postgres，含 000022 migration）：
/// repo Replace 語意（整批取代）+ 候選 query 正確標記 PrefersThisBoss / HasAnyPreference。
/// 排序本身在 service（LINQ），由單元測試覆蓋；這裡驗「旗標從 DB 正確取回」。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class PreferredBossIntegrationTests
{
    private readonly PostgresFixture _fx;
    public PreferredBossIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Replace_RoundTrips_AndReplacesOldSet()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        await Seed.PlayerAsync(cs, 500, "P500");
        await Seed.CharacterAsync(cs, "c500", 500, "C500", "英雄", 900);
        var b1 = await Seed.BossAsync(cs, "B1");
        var b2 = await Seed.BossAsync(cs, "B2");
        var b3 = await Seed.BossAsync(cs, "B3");

        var repo = new CharacterPreferredBossRepository(_fx.CreateDbContext());

        await repo.ReplaceAsync("c500", new[] { b1, b2 });
        Assert.Equal(new[] { b1, b2 }, (await repo.GetBossIdsByCharacterAsync("c500")).OrderBy(x => x).ToArray());

        // Replace = 刪舊插新：b1 應消失、b3 進來
        await repo.ReplaceAsync("c500", new[] { b2, b3 });
        Assert.Equal(new[] { b2, b3 }, (await repo.GetBossIdsByCharacterAsync("c500")).OrderBy(x => x).ToArray());

        // 空集合 = 清空
        await repo.ReplaceAsync("c500", System.Array.Empty<int>());
        Assert.Empty(await repo.GetBossIdsByCharacterAsync("c500"));
    }

    [Fact]
    public async Task Pool_MarksPrefersThisBoss_AndHasAnyPreference()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var boss = await Seed.BossAsync(cs, "拉圖斯");
        await Seed.PlayerAsync(cs, 501, "P501");
        await Seed.CharacterAsync(cs, "cPref", 501, "偏好本王", "英雄", 900);
        await Seed.PlayerAsync(cs, 502, "P502");
        await Seed.CharacterAsync(cs, "cNone", 502, "沒偏好", "英雄", 900);

        var db = _fx.CreateDbContext();
        await db.ExecuteAsync("""UPDATE "Character" SET "IsSeekingRaid"=true WHERE "Id" IN ('cPref','cNone');""", new { });
        await db.ExecuteAsync(
            """
            INSERT INTO "PlayerAvailabilityStanding"("DiscordId","Weekday","StartTime","EndTime")
            SELECT d, gs, TIME '00:00', TIME '00:00' FROM (VALUES (501::bigint),(502::bigint)) v(d), generate_series(1,7) gs;
            """, new { });
        await db.ExecuteAsync("""INSERT INTO "CharacterPreferredBoss"("CharacterId","BossId") VALUES ('cPref', @boss);""", new { boss });

        var pool = (await new TeamCandidateQuery(_fx.CreateDbContext()).GetPoolAsync(boss)).ToList();

        var pref = pool.Single(x => x.CharacterId == "cPref");
        var none = pool.Single(x => x.CharacterId == "cNone");
        Assert.True(pref.PrefersThisBoss);
        Assert.True(pref.HasAnyPreference);
        Assert.False(none.PrefersThisBoss);
        Assert.False(none.HasAnyPreference);
    }
}
