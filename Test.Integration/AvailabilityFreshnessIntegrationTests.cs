using Infrastructure.Query;
using Infrastructure.Repositories;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 常設可用時段新鮮度衰退資料層整合測試（真 Postgres，含 000025 migration；見
/// plans/2026-09-01-availability-freshness-decay.md）：候選池 GetPoolAsync 依 LastAffirmedAt 濾掉 stale opt-in，
/// NULL 視為永久新鮮（backfill 保守）。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class AvailabilityFreshnessIntegrationTests
{
    private readonly PostgresFixture _fx;
    public AvailabilityFreshnessIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Pool_ExcludesStaleOptIn_ButKeepsFreshAndNull()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var boss = await Seed.BossAsync(cs, "史烏");
        await Seed.PlayerAsync(cs, 601, "PFresh");
        await Seed.CharacterAsync(cs, "cFresh", 601, "新鮮", "英雄", 900);
        await Seed.PlayerAsync(cs, 602, "PStale");
        await Seed.CharacterAsync(cs, "cStale", 602, "殭屍", "英雄", 900);
        await Seed.PlayerAsync(cs, 603, "PNull");
        await Seed.CharacterAsync(cs, "cNull", 603, "從未動作", "英雄", 900);

        var db = _fx.CreateDbContext();
        await db.ExecuteAsync("""UPDATE "Character" SET "IsSeekingRaid"=true WHERE "Id" IN ('cFresh','cStale','cNull');""", new { });
        await db.ExecuteAsync(
            """
            INSERT INTO "PlayerAvailabilityStanding"("DiscordId","Weekday","StartTime","EndTime")
            SELECT d, gs, TIME '00:00', TIME '00:00' FROM (VALUES (601::bigint),(602::bigint),(603::bigint)) v(d), generate_series(1,7) gs;
            """, new { });
        // 601 今天有動作（新鮮）、602 40 天前（> 30 天門檻＝殭屍）、603 保持 NULL（從未 bump ＝ 永久新鮮）
        await db.ExecuteAsync("""UPDATE "Player" SET "LastAffirmedAt" = now()                     WHERE "DiscordId" = 601;""", new { });
        await db.ExecuteAsync("""UPDATE "Player" SET "LastAffirmedAt" = now() - interval '40 days' WHERE "DiscordId" = 602;""", new { });

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var ids = (await new TeamCandidateQuery(_fx.CreateDbContext()).GetPoolAsync(boss, cutoff))
            .Select(x => x.CharacterId).ToHashSet();

        Assert.Contains("cFresh", ids);        // 今天有動作 → 留
        Assert.Contains("cNull", ids);         // 從未 bump（NULL）→ 視為永久新鮮 → 留
        Assert.DoesNotContain("cStale", ids);  // 40 天前 > 30 天門檻 → 濾掉
    }

    [Fact]
    public async Task NudgeTargets_OnlyStaleSeekingNotYetNudged()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        // 五種情境，只有「快過期 + 參戰 + 提醒後又有活動（此處＝從未提醒）」該入選
        foreach (var (id, name) in new[] { (701, "target"), (702, "fresh"), (703, "nudged"), (704, "notseeking"), (705, "null") })
        {
            await Seed.PlayerAsync(cs, id, name);
            await Seed.CharacterAsync(cs, $"c{id}", id, name, "英雄", 900);
        }

        var db = _fx.CreateDbContext();
        // 704 不參戰；其餘參戰
        await db.ExecuteAsync("""UPDATE "Character" SET "IsSeekingRaid"=true WHERE "Id" IN ('c701','c702','c703','c705');""", new { });
        await db.ExecuteAsync("""UPDATE "Player" SET "LastAffirmedAt" = now() - interval '40 days' WHERE "DiscordId" IN (701,703,704);""", new { });
        await db.ExecuteAsync("""UPDATE "Player" SET "LastAffirmedAt" = now()                     WHERE "DiscordId" = 702;""", new { });
        // 703：已在「最後活動之後」提醒過（10 天前 > 40 天前的活動）→ 不該再提醒
        await db.ExecuteAsync("""UPDATE "Player" SET "FreshnessNudgedAt" = now() - interval '10 days' WHERE "DiscordId" = 703;""", new { });
        // 705 保持 LastAffirmedAt NULL（永久新鮮）

        var targets = await new PlayerRepository(_fx.CreateDbContext()).GetFreshnessNudgeTargetsAsync(27); // 門檻30 − 前置3

        Assert.Contains(701UL, targets);          // 快過期 + 參戰 + 未提醒 → 入選
        Assert.DoesNotContain(702UL, targets);    // 新鮮 → 不
        Assert.DoesNotContain(703UL, targets);    // 提醒後無新活動 → 不
        Assert.DoesNotContain(704UL, targets);    // 不參戰 → 不
        Assert.DoesNotContain(705UL, targets);    // NULL（永久新鮮）→ 不
    }
}
