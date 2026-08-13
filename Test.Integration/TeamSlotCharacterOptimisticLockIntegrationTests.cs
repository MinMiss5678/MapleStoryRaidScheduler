using Dapper;
using Domain.Entities;
using Infrastructure.Repositories;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 驗樂觀鎖真的對真 Postgres 生效：xmin 版本比對正確擋下過時覆寫，正確版本能成功更新。
/// mock 測只驗得到「service 邏輯有沒有把衝突塞進清單」，驗不到 xmin::text/@version::xid 這段
/// 原生 SQL 轉型跟比對本身是否正確——這裡才是真正釘住這個事實的地方。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class TeamSlotCharacterOptimisticLockIntegrationTests
{
    private readonly PostgresFixture _fx;
    public TeamSlotCharacterOptimisticLockIntegrationTests(PostgresFixture fx) => _fx = fx;

    private async Task<string> GetCurrentVersionAsync(string cs, int characterRowId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        return (await conn.ExecuteScalarAsync<string?>(
            """SELECT xmin::text FROM "TeamSlotCharacter" WHERE "Id" = @id""", new { id = characterRowId }))!;
    }

    [Fact]
    public async Task UpdateAsync_ReturnsFalse_WhenVersionIsStale()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs);
        var teamSlotId = await Seed.TeamSlotAsync(cs, bossId, "auto");
        var characterRowId = await Seed.OccupiedSlotAsync(cs, teamSlotId, discordId: 111, charId: "occ1");

        var originalVersion = await GetCurrentVersionAsync(cs, characterRowId);
        var repo = new TeamSlotCharacterRepository(_fx.CreateDbContext());

        // 第一次用正確版本更新 → 成功，xmin 會跟著換新
        var firstAttempt = await repo.UpdateAsync(new TeamSlotCharacter
        {
            Id = characterRowId,
            TeamSlotId = teamSlotId,
            DiscordId = 111,
            DiscordName = "P",
            CharacterId = "occ1",
            Job = "Warrior",
            AttackPower = 999,
            Version = originalVersion
        });
        Assert.True(firstAttempt);

        // 第二次還拿著「第一次更新前」的舊版本 → 應被擋下（xmin 已經變了）
        var staleAttempt = await repo.UpdateAsync(new TeamSlotCharacter
        {
            Id = characterRowId,
            TeamSlotId = teamSlotId,
            DiscordId = 111,
            DiscordName = "P",
            CharacterId = "occ1",
            Job = "Bishop",
            AttackPower = 1,
            Version = originalVersion
        });
        Assert.False(staleAttempt);

        // 資料應停在第一次更新後的狀態（Bishop/1 那次過時寫入沒有生效）
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var job = await conn.ExecuteScalarAsync<string>(
            """SELECT "Job" FROM "TeamSlotCharacter" WHERE "Id" = @id""", new { id = characterRowId });
        Assert.Equal("Warrior", job);
    }

    /// <summary>
    /// row 被別人整筆刪掉（不是版本不對，是 Id 根本不存在了）——
    /// UPDATE ... WHERE Id=@id AND xmin=@version 該不該跟「row 還在但版本不對」走到同一個結果（0 筆受影響）？
    /// 兩者對 WHERE 子句而言都是「找不到符合的列」，但這是 SQL 的行為，值得實測釘住而不是推論。
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsFalse_WhenRowDoesNotExist()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs);
        var teamSlotId = await Seed.TeamSlotAsync(cs, bossId, "auto");
        var characterRowId = await Seed.OccupiedSlotAsync(cs, teamSlotId, discordId: 111, charId: "occ1");
        var staleVersion = await GetCurrentVersionAsync(cs, characterRowId);

        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("""DELETE FROM "TeamSlotCharacter" WHERE "Id" = @id""", new { id = characterRowId });
        }

        var repo = new TeamSlotCharacterRepository(_fx.CreateDbContext());
        var result = await repo.UpdateAsync(new TeamSlotCharacter
        {
            Id = characterRowId,
            TeamSlotId = teamSlotId,
            DiscordId = 111,
            DiscordName = "P",
            CharacterId = "occ1",
            Job = "Bishop",
            Version = staleVersion
        });

        Assert.False(result);
    }
}
