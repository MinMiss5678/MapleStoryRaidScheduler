using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class SessionServiceTests
{
    private readonly Mock<ISessionRepository> _sessionRepoMock = new();
    private readonly Mock<ISessionQuery> _sessionQueryMock = new();
    private readonly FakeSessionCache _sessionCache = new();
    private readonly SessionService _sessionService;

    public SessionServiceTests()
    {
        // 解耦後 SessionService 不再依賴 IDiscordOAuthClient（不再用 RefreshToken 續期）
        _sessionService = new SessionService(
            _sessionRepoMock.Object,
            _sessionQueryMock.Object,
            _sessionCache);
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
        _sessionQueryMock.Setup(q => q.GetAsync("sid-cache")).ReturnsAsync(session);

        var result1 = await _sessionService.GetAsync("sid-cache", "789"); // DB
        var result2 = await _sessionService.GetAsync("sid-cache", "789"); // 快取

        Assert.NotNull(result1);
        Assert.NotNull(result2);
        _sessionQueryMock.Verify(q => q.GetAsync("sid-cache"), Times.Once); // 命中快取不再查 DB
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
}
