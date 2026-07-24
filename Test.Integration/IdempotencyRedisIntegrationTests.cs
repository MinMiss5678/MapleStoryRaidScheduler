using Application.Interface;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Test.Integration;

/// <summary>
/// RedisIdempotencyStore 打真 Redis（Testcontainers）：驗 SET NX 去重語意，含「跨連線＝跨 pod」互斥。
/// </summary>
public class IdempotencyRedisIntegrationTests : IClassFixture<RedisFixture>
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private readonly RedisFixture _fx;

    public IdempotencyRedisIntegrationTests(RedisFixture fx) => _fx = fx;

    private static IIdempotencyStore StoreOn(IConnectionMultiplexer redis)
        => new RedisIdempotencyStore(redis, NullLogger<RedisIdempotencyStore>.Instance);

    [Fact]
    public async Task 第一次_true_同key重複_false()
    {
        using var redis = _fx.Connect();
        var store = StoreOn(redis);
        var key = Guid.NewGuid().ToString();

        Assert.True(await store.TryMarkAsync(key, Ttl));   // 第一次 → 放行
        Assert.False(await store.TryMarkAsync(key, Ttl));  // 重複 → 擋
    }

    [Fact]
    public async Task 不同key_都_true()
    {
        using var redis = _fx.Connect();
        var store = StoreOn(redis);

        Assert.True(await store.TryMarkAsync(Guid.NewGuid().ToString(), Ttl));
        Assert.True(await store.TryMarkAsync(Guid.NewGuid().ToString(), Ttl));
    }

    [Fact]
    public async Task 跨連線_模擬跨pod_同key第二個_false()
    {
        // 兩條獨立連線（不同 multiplexer）= 兩個 pod，共用同一 Redis
        using var redisA = _fx.Connect();
        using var redisB = _fx.Connect();
        var key = Guid.NewGuid().ToString();

        Assert.True(await StoreOn(redisA).TryMarkAsync(key, Ttl));   // pod A 第一次 → 放行
        Assert.False(await StoreOn(redisB).TryMarkAsync(key, Ttl));  // pod B 同 key → 擋（跨 pod de-dup 成立）
    }
}
