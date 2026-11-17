using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Test;

public class SessionServiceTests
{
    private readonly Mock<ISessionRepository> _sessionRepoMock = new();
    private readonly Mock<ISessionQuery> _sessionQueryMock = new();
    private readonly FakeSessionCache _sessionCache = new();
    private readonly CapturingLogger<SessionService> _logger = new();
    private readonly SessionService _sessionService;

    public SessionServiceTests()
    {
        // 解耦後 SessionService 不再依賴 IDiscordOAuthClient（不再用 RefreshToken 續期）
        _sessionService = new SessionService(
            _sessionRepoMock.Object,
            _sessionQueryMock.Object,
            _sessionCache,
            _logger);
    }

    // 以字典模擬 Redis 共享快取（Get/Set/Remove），讓「命中快取不再查 DB」等測試成立。
    private sealed class FakeSessionCache : ISessionCache
    {
        private readonly Dictionary<string, Session> _store = new();

        public Task<Session?> GetAsync(string discordId)
            => Task.FromResult(_store.TryGetValue(discordId, out var s) ? s : null);

        public Task SetAsync(string discordId, Session session, TimeSpan ttl)
        {
            _store[discordId] = session;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string discordId)
        {
            _store.Remove(discordId);
            return Task.CompletedTask;
        }
    }

    private static Session ValidSession(ulong discordId) => new()
    {
        SessionId = "",
        DiscordId = discordId,
        SessionExpiry = DateTimeOffset.UtcNow.AddDays(30) // session 有效（我的政策）
    };

    [Fact]
    public async Task CreateAsync_CreatesSessionAndReturnsId()
    {
        _sessionRepoMock.Setup(r => r.CreateAsync(It.IsAny<string>(), 123UL)).ReturnsAsync(1);

        var sessionId = await _sessionService.CreateAsync(123UL);

        Assert.NotEmpty(sessionId);
        Assert.Equal(32, sessionId.Length); // Guid.NewGuid().ToString("N") = 32 chars
    }

    [Fact]
    public async Task GetAsync_SessionNotFound_ReturnsNull()
    {
        _sessionQueryMock.Setup(q => q.GetAsync("invalid-session")).ReturnsAsync((Session?)null);

        var result = await _sessionService.GetAsync("invalid-session", "999");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ValidSession_ReturnsSession()
    {
        var session = ValidSession(456UL);
        _sessionQueryMock.Setup(q => q.GetAsync("sid-123")).ReturnsAsync(session);

        var result = await _sessionService.GetAsync("sid-123", "456");

        Assert.NotNull(result);
        Assert.Equal(456UL, result.DiscordId);
    }

    [Fact]
    public async Task GetAsync_CachedSession_ReturnsCachedResult()
    {
        var session = ValidSession(789UL);
        session.SessionId = "sid-cache";
        _sessionQueryMock.Setup(q => q.GetAsync("sid-cache")).ReturnsAsync(session);

        var result1 = await _sessionService.GetAsync("sid-cache", "789"); // DB
        var result2 = await _sessionService.GetAsync("sid-cache", "789"); // 快取

        Assert.NotNull(result1);
        Assert.NotNull(result2);
        _sessionQueryMock.Verify(q => q.GetAsync("sid-cache"), Times.Once); // 命中快取不再查 DB
    }

    // ★ 攻擊測試：快取以 discordId 為 key，命中時「不比對 sessionId」= session 冒用漏洞。
    // 攻擊情境：知道某人的 discordId、其 session 又在快取熱窗口內 → 帶「任意/偽造的 sessionId」也能通過。
    // 此測試證明修正（cached.SessionId != sessionId → null）真的擋住攻擊；移掉該比對這條會變紅。
    [Fact]
    public async Task GetAsync_快取命中_sessionId不符_回Null_擋session冒用()
    {
        // Arrange：先用「正確 sessionId」讓快取熱起來（快取以 discordId=789 為 key）
        var session = ValidSession(789UL);
        session.SessionId = "sid-real";
        _sessionQueryMock.Setup(q => q.GetAsync("sid-real")).ReturnsAsync(session);
        Assert.NotNull(await _sessionService.GetAsync("sid-real", "789")); // 走 DB、寫入快取

        // Act：攻擊者知道 discordId=789，帶「偽造的 sessionId」打過來（此時快取是熱的）
        var forged = await _sessionService.GetAsync("sid-forged", "789");

        // Assert：必須被擋（回 null）——不能因為 discordId 的快取命中就放行
        Assert.Null(forged);
    }

    [Fact]
    public async Task GetAsync_過SessionExpiry_回Null_且不刷新Discord()
    {
        // 過了 SessionExpiry → 失效；不再打 Discord 續期（解耦後根本沒有 Discord 依賴）
        var expired = ValidSession(555UL);
        expired.SessionExpiry = DateTimeOffset.UtcNow.AddMinutes(-5);
        _sessionQueryMock.Setup(q => q.GetAsync("sid-exp")).ReturnsAsync(expired);

        var result = await _sessionService.GetAsync("sid-exp", "555");

        Assert.Null(result); // 過期即失效；解耦後根本沒有刷新路徑（無 Discord 依賴）
    }

    [Fact]
    public async Task GetAsync_近到期_延展SessionExpiry並寫DB()
    {
        // 剩餘 5 天 < 門檻 15 天 → 活動時延展成 now + 30 天（節流 sliding 觸發）
        var nearExpiry = ValidSession(456UL);
        nearExpiry.SessionExpiry = DateTimeOffset.UtcNow.AddDays(5);
        _sessionQueryMock.Setup(q => q.GetAsync("sid-slide")).ReturnsAsync(nearExpiry);
        _sessionRepoMock.Setup(r => r.ExtendAsync("sid-slide", It.IsAny<DateTimeOffset>())).ReturnsAsync(1);

        var result = await _sessionService.GetAsync("sid-slide", "456");

        Assert.NotNull(result);
        Assert.True(result.SessionExpiry > DateTimeOffset.UtcNow.AddDays(20)); // 已延展
        _sessionRepoMock.Verify(r => r.ExtendAsync("sid-slide", It.IsAny<DateTimeOffset>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_離到期還遠_不延展()
    {
        // ValidSession 的 SessionExpiry = +30 天 > 門檻 → 純讀、不寫（節流：不打架讀穿快取）
        var session = ValidSession(789UL);
        _sessionQueryMock.Setup(q => q.GetAsync("sid-noslide")).ReturnsAsync(session);

        var result = await _sessionService.GetAsync("sid-noslide", "789");

        Assert.NotNull(result);
        _sessionRepoMock.Verify(r => r.ExtendAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_RemovesCacheAndDeletesFromRepo()
    {
        _sessionRepoMock.Setup(r => r.DeleteAsync("sid-del")).ReturnsAsync(true);

        var result = await _sessionService.DeleteAsync("sid-del", "111");

        Assert.True(result);
        _sessionRepoMock.Verify(r => r.DeleteAsync("sid-del"), Times.Once);
    }

    [Fact]
    public async Task DeleteByDiscordAsync_RemovesCacheAndDeletesFromRepo()
    {
        _sessionRepoMock.Setup(r => r.DeleteByDiscordAsync(222UL)).Returns(Task.CompletedTask);

        await _sessionService.DeleteByDiscordAsync(222UL);

        _sessionRepoMock.Verify(r => r.DeleteByDiscordAsync(222UL), Times.Once);
    }

    // ===== 同一管理員多個 session ＋ 快取歸屬（plans/2026-11-04-session-multi-device-relogin.md §三 A 組）=====
    // 快取以 discordId 為 key、只存一筆；資料庫允許同一人多筆 session（每登入一次一筆）。
    // 快取命中但 sessionId 不符時，要回資料庫確認（第二台裝置的合法 session），且查到的 session 必須屬於 cookie 的 discordId。

    private static Session SessionOf(string sessionId, ulong owner, bool expired = false) => new()
    {
        SessionId = sessionId,
        DiscordId = owner,
        SessionExpiry = expired ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddDays(30)
    };

    /// <summary>先用 sid-real 走一次資料庫，讓快取記住 discordId=789 → sid-real。</summary>
    private async Task WarmCacheWithRealSessionAsync()
    {
        _sessionQueryMock.Setup(q => q.GetAsync("sid-real")).ReturnsAsync(SessionOf("sid-real", 789UL));
        Assert.NotNull(await _sessionService.GetAsync("sid-real", "789"));
    }

    [Fact]
    public async Task A1_快取命中_sessionId不符_資料庫有同一人的有效session_回傳該session()
    {
        await WarmCacheWithRealSessionAsync();
        _sessionQueryMock.Setup(q => q.GetAsync("sid-second")).ReturnsAsync(SessionOf("sid-second", 789UL));

        var result = await _sessionService.GetAsync("sid-second", "789");

        Assert.NotNull(result);
        Assert.Equal("sid-second", result.SessionId);
        Assert.Equal(789UL, result.DiscordId);
    }

    [Fact]
    public async Task A3_快取命中_sessionId不符_資料庫的session已過期_回Null()
    {
        await WarmCacheWithRealSessionAsync();
        _sessionQueryMock.Setup(q => q.GetAsync("sid-second"))
            .ReturnsAsync(SessionOf("sid-second", 789UL, expired: true));

        Assert.Null(await _sessionService.GetAsync("sid-second", "789"));
    }

    [Fact]
    public async Task A4_快取命中_sessionId不符_資料庫的session屬於另一人_回Null()
    {
        await WarmCacheWithRealSessionAsync();
        _sessionQueryMock.Setup(q => q.GetAsync("sid-other")).ReturnsAsync(SessionOf("sid-other", 999UL));

        Assert.Null(await _sessionService.GetAsync("sid-other", "789"));
    }

    [Fact]
    public async Task A5_快取未命中_資料庫的session屬於另一人_回Null且不寫入快取()
    {
        // 攻擊情境：管理員 999 用自己的 sessionId，但 cookie 的 discordId 填 789 → 不得把自己的 session 寫進 789 的快取位置
        _sessionQueryMock.Setup(q => q.GetAsync("sid-other")).ReturnsAsync(SessionOf("sid-other", 999UL));

        var result = await _sessionService.GetAsync("sid-other", "789");

        Assert.Null(result);
        Assert.Null(await _sessionCache.GetAsync("789"));
    }

    [Fact]
    public async Task A7_快取命中_sessionId不符_資料庫確認有效後放行_但不覆寫快取()
    {
        await WarmCacheWithRealSessionAsync();
        _sessionQueryMock.Setup(q => q.GetAsync("sid-second")).ReturnsAsync(SessionOf("sid-second", 789UL));

        Assert.NotNull(await _sessionService.GetAsync("sid-second", "789"));

        var cached = await _sessionCache.GetAsync("789");
        Assert.NotNull(cached);
        Assert.Equal("sid-real", cached.SessionId); // 快取仍是原本那筆（A-2：不覆寫，避免兩台裝置交替時快取來回翻）
    }

    // ===== session 無效的細分原因（plans/2026-11-16-auth-401-reason-and-commit-before-response.md §二 B 組）=====

    [Fact]
    public async Task B1_查無session_記錄原因()
    {
        _sessionQueryMock.Setup(q => q.GetAsync("sid-missing")).ReturnsAsync((Session?)null);

        Assert.Null(await _sessionService.GetAsync("sid-missing", "789"));

        var log = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, log.Level);
        Assert.Contains("查無 session", log.Message);
    }

    [Fact]
    public async Task B2_session歸屬不符_記Warning_含兩個discordId()
    {
        _sessionQueryMock.Setup(q => q.GetAsync("sid-other")).ReturnsAsync(SessionOf("sid-other", 999UL));

        Assert.Null(await _sessionService.GetAsync("sid-other", "789"));

        var log = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Contains("session 歸屬不符", log.Message);
        Assert.Contains("789", log.Message);
        Assert.Contains("999", log.Message);
    }

    [Fact]
    public async Task B3_session已過期_記錄原因()
    {
        _sessionQueryMock.Setup(q => q.GetAsync("sid-exp")).ReturnsAsync(SessionOf("sid-exp", 789UL, expired: true));

        Assert.Null(await _sessionService.GetAsync("sid-exp", "789"));

        var log = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, log.Level);
        Assert.Contains("session 已過期", log.Message);
    }

    [Fact]
    public async Task B4_無效原因的log_不含sessionId的值()
    {
        _sessionQueryMock.Setup(q => q.GetAsync("secret-a")).ReturnsAsync((Session?)null);
        _sessionQueryMock.Setup(q => q.GetAsync("secret-b")).ReturnsAsync(SessionOf("secret-b", 999UL));
        _sessionQueryMock.Setup(q => q.GetAsync("secret-c")).ReturnsAsync(SessionOf("secret-c", 789UL, expired: true));

        await _sessionService.GetAsync("secret-a", "789");
        await _sessionService.GetAsync("secret-b", "789");
        await _sessionService.GetAsync("secret-c", "789");

        Assert.Equal(3, _logger.Entries.Count);
        Assert.DoesNotContain("secret-", _logger.AllText());
    }
}
