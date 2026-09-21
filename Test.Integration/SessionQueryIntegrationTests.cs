using Dapper;
using Infrastructure.Query;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 驗 SessionQuery.GetAsync 的 SQL 真的把 "SessionId" 欄位撈出並對映到 Session.SessionId。
/// 這是 SessionService cache-hit 比對（cached.SessionId != sessionId）能成立的前提——
/// 若 SELECT 漏掉 SessionId，映射出的物件 SessionId 為 null，快取回填後合法使用者第二次請求
/// （cache 命中）會被 null != 真實 id 誤判成失效 → 403。此類「SQL 少 SELECT 一欄」的縫，
/// 單元測試因 mock 掉 ISessionQuery 而看不到，只有打真 DB 的整合測試守得住（見 Testing.md：mock 測接線圖不測邏輯）。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class SessionQueryIntegrationTests
{
    private readonly PostgresFixture _fx;
    public SessionQueryIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task GetAsync_PopulatesSessionId_ForCacheHitComparison()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var expiry = DateTimeOffset.UtcNow.AddDays(30);
        await using (var c = new NpgsqlConnection(cs))
        {
            await c.OpenAsync();
            await c.ExecuteAsync(
                """INSERT INTO "Session"("SessionId","DiscordId","SessionExpiry") VALUES (@sid,@did,@exp);""",
                new { sid = "sid-abc", did = 555L, exp = expiry });
        }

        var session = await new SessionQuery(_fx.CreateDbContext()).GetAsync("sid-abc");

        Assert.NotNull(session);
        // ★ 關鍵斷言：SELECT 漏掉 SessionId 時，這裡會是 null → 測試變紅（正是要守的縫）。
        Assert.Equal("sid-abc", session.SessionId);
        Assert.Equal(555UL, session.DiscordId);
        Assert.True(session.SessionExpiry > DateTimeOffset.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenSessionIdNotFound()
    {
        await _fx.ResetAsync();

        var session = await new SessionQuery(_fx.CreateDbContext()).GetAsync("nonexistent");

        Assert.Null(session);
    }
}
