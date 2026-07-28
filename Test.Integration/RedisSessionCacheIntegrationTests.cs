using Domain.Entities;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Test.Integration;

/// <summary>
/// RedisSessionCache 打真 Redis：驗 set/get round-trip + 「撤銷跨連線＝跨 pod 立即生效」（分散式），
/// 即 Phase 3 要修的多 pod gap——某 pod 撤 session，其他 pod 立刻讀不到，不必等 TTL。
/// </summary>
public class RedisSessionCacheIntegrationTests : IClassFixture<RedisFixture>
{
    private readonly RedisFixture _fx;

    public RedisSessionCacheIntegrationTests(RedisFixture fx) => _fx = fx;

    private static RedisSessionCache Cache(IConnectionMultiplexer redis)
        => new(redis, NullLogger<RedisSessionCache>.Instance);

    private static Session NewSession(ulong discordId) => new()
    {
        SessionId = Guid.NewGuid().ToString("N"),
        DiscordId = discordId,
        SessionExpiry = DateTimeOffset.UtcNow.AddHours(1)
    };

    [Fact]
    public async Task Set後Get拿得回同一份()
    {
        using var redis = _fx.Connect();
        var cache = Cache(redis);
        var discordId = Guid.NewGuid().ToString();
        var session = NewSession(123);

        await cache.SetAsync(discordId, session, TimeSpan.FromMinutes(5));
        var got = await cache.GetAsync(discordId);

        Assert.NotNull(got);
        Assert.Equal(session.SessionId, got!.SessionId);
        Assert.Equal(123UL, got.DiscordId);
    }

    [Fact]
    public async Task 撤銷跨連線_模擬跨pod_立即生效()
    {
        using var redisA = _fx.Connect();
        using var redisB = _fx.Connect();
        var cacheA = Cache(redisA);
        var cacheB = Cache(redisB);
        var discordId = Guid.NewGuid().ToString();

        await cacheA.SetAsync(discordId, NewSession(456), TimeSpan.FromMinutes(5));
        Assert.NotNull(await cacheB.GetAsync(discordId));   // pod B 讀得到 pod A 寫的（共享）

        await cacheA.RemoveAsync(discordId);                // pod A 撤銷
        Assert.Null(await cacheB.GetAsync(discordId));      // pod B 立即讀不到（跨 pod 生效，非等 TTL）
    }
}
