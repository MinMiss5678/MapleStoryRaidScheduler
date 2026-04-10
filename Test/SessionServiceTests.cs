using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace Test;

public class SessionServiceTests
{
    private readonly Mock<ISessionRepository> _sessionRepoMock = new();
    private readonly Mock<ISessionQuery> _sessionQueryMock = new();
    private readonly Mock<IDiscordOAuthClient> _discordClientMock = new();
    private readonly IMemoryCache _memoryCache;
    private readonly SessionService _sessionService;

    public SessionServiceTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _sessionService = new SessionService(
            _sessionRepoMock.Object,
            _sessionQueryMock.Object,
            _discordClientMock.Object,
            _memoryCache);
    }

    [Fact]
    public async Task CreateAsync_CreatesSessionAndReturnsId()
    {
        var token = new DiscordToken { AccessToken = "acc", RefreshToken = "ref", ExpiresIn = 3600 };
        _sessionRepoMock.Setup(r => r.CreateAsync(It.IsAny<string>(), 123UL, token)).ReturnsAsync(1);

        var sessionId = await _sessionService.CreateAsync(123UL, token);

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
    public async Task GetAsync_ValidSession_NotExpired_ReturnsSession()
    {
        var session = new Session
        {
            DiscordId = 456UL,
            AccessToken = "valid-token",
            RefreshToken = "ref",
            Expiry = DateTimeOffset.UtcNow.AddHours(1) // 未過期
        };
        _sessionQueryMock.Setup(q => q.GetAsync("sid-123")).ReturnsAsync(session);

        var result = await _sessionService.GetAsync("sid-123", "456");

        Assert.NotNull(result);
        Assert.Equal(456UL, result.DiscordId);
    }

    [Fact]
    public async Task GetAsync_CachedSession_ReturnsCachedResult()
    {
        var session = new Session
        {
            DiscordId = 789UL,
            AccessToken = "cached-token",
            RefreshToken = "ref",
            Expiry = DateTimeOffset.UtcNow.AddHours(1)
        };
        _sessionQueryMock.Setup(q => q.GetAsync("sid-cache")).ReturnsAsync(session);

        // 第一次呼叫 - 從 DB
        var result1 = await _sessionService.GetAsync("sid-cache", "789");
        // 第二次呼叫 - 應從快取
        var result2 = await _sessionService.GetAsync("sid-cache", "789");

        Assert.NotNull(result1);
        Assert.NotNull(result2);
        // 快取後第二次不再呼叫 DB
        _sessionQueryMock.Verify(q => q.GetAsync("sid-cache"), Times.Once);
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
