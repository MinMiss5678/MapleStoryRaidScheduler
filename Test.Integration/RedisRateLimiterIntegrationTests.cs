using Microsoft.Extensions.Logging.Abstractions;
using Presentation.WebApi.RateLimiting;
using StackExchange.Redis;
using Xunit;

namespace Test.Integration;

/// <summary>
/// RedisFixedWindowRateLimiter 打真 Redis：驗固定視窗上限 + 「跨連線＝跨 pod」共用同一計數（分散式）。
/// </summary>
public class RedisRateLimiterIntegrationTests : IClassFixture<RedisFixture>
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    private readonly RedisFixture _fx;

    public RedisRateLimiterIntegrationTests(RedisFixture fx) => _fx = fx;

    private static RedisFixedWindowRateLimiter Limiter(IConnectionMultiplexer redis, string key, int limit)
        => new(redis, NullLogger.Instance, key, limit, Window);

    [Fact]
    public async Task 視窗內達上限後被擋()
    {
        using var redis = _fx.Connect();
        var limiter = Limiter(redis, $"rl:{Guid.NewGuid()}", limit: 3);

        for (int i = 0; i < 3; i++)
            Assert.True((await limiter.AcquireAsync(1)).IsAcquired);   // 前 3 次放行
        Assert.False((await limiter.AcquireAsync(1)).IsAcquired);      // 第 4 次超限 → 擋
    }

    [Fact]
    public async Task 跨連線_模擬跨pod_共用同一計數()
    {
        using var redisA = _fx.Connect();
        using var redisB = _fx.Connect();
        var key = $"rl:{Guid.NewGuid()}";
        var a = Limiter(redisA, key, 2);
        var b = Limiter(redisB, key, 2);

        Assert.True((await a.AcquireAsync(1)).IsAcquired);   // pod A：計數 1
        Assert.True((await b.AcquireAsync(1)).IsAcquired);   // pod B：計數 2（跨 pod 共用）
        Assert.False((await a.AcquireAsync(1)).IsAcquired);  // pod A：計數 3 → 超限（上限跨 pod 成立）
    }
}
