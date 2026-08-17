using Dapper;
using Domain.Entities;
using Infrastructure.Repositories;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 即時找隊意圖 upsert 去重（migration 000020 的 uq_lfgintent_char_boss，NULLS NOT DISTINCT）：
/// 同角色對同一王（含任意王=NULL）重貼只會刷新 TTL、不新增列。跑真 Postgres。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class LfgIntentRepositoryIntegrationTests
{
    private readonly PostgresFixture _fx;
    public LfgIntentRepositoryIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task CreateAsync_SameCharacterSameBoss_UpsertsToSingleRow_AndRefreshesTtl()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs);
        await Seed.PlayerAsync(cs, 7002, "Luna");
        await Seed.CharacterAsync(cs, "c7002", 7002, "暗夜刺客", "夜使者", 105000);

        var repo = new LfgIntentRepository(_fx.CreateDbContext());
        var t1 = DateTimeOffset.UtcNow.AddMinutes(10);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(45);
        await repo.CreateAsync(new LfgIntent { DiscordId = 7002, CharacterId = "c7002", BossId = bossId, ExpiresAt = t1 });
        await repo.CreateAsync(new LfgIntent { DiscordId = 7002, CharacterId = "c7002", BossId = bossId, ExpiresAt = t2 });

        await using var conn = new NpgsqlConnection(cs);
        var count = await conn.ExecuteScalarAsync<int>(
            """SELECT count(*) FROM "LfgIntent" WHERE "CharacterId"='c7002' AND "BossId"=@bossId""", new { bossId });
        Assert.Equal(1, count); // 只 1 列
        var exp = await conn.ExecuteScalarAsync<DateTime>(
            """SELECT "ExpiresAt" FROM "LfgIntent" WHERE "CharacterId"='c7002' AND "BossId"=@bossId""", new { bossId });
        Assert.True(exp > t1.UtcDateTime.AddMinutes(20)); // TTL 已刷新到 t2
    }

    [Fact]
    public async Task CreateAsync_AnyBossNullBossId_AlsoDedupedToSingleRow()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        await Seed.BossAsync(cs);
        await Seed.PlayerAsync(cs, 7001, "Karl");
        await Seed.CharacterAsync(cs, "c7001", 7001, "聖光牧師", "主教", 120000);

        var repo = new LfgIntentRepository(_fx.CreateDbContext());
        await repo.CreateAsync(new LfgIntent { DiscordId = 7001, CharacterId = "c7001", BossId = null, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10) });
        await repo.CreateAsync(new LfgIntent { DiscordId = 7001, CharacterId = "c7001", BossId = null, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10) });

        await using var conn = new NpgsqlConnection(cs);
        var count = await conn.ExecuteScalarAsync<int>(
            """SELECT count(*) FROM "LfgIntent" WHERE "CharacterId"='c7001' AND "BossId" IS NULL""");
        Assert.Equal(1, count); // NULLS NOT DISTINCT：任意王(null) 也視為同一組
    }
}
