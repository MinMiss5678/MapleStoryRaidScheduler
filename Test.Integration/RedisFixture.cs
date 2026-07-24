using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 起一顆拋棄式 Redis 容器供 idempotency 去重整合測試用。
/// </summary>
public class RedisFixture : IAsyncLifetime
{
#pragma warning disable CS0618 // 無參數 builder 被標 obsolete，但 image 由 .WithImage 指定，沿用官方標準用法
    private readonly RedisContainer _container = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();
#pragma warning restore CS0618

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>建立一條指向測試容器的連線（呼叫端負責 Dispose）。多條連線可模擬多 pod。</summary>
    public IConnectionMultiplexer Connect() => ConnectionMultiplexer.Connect(ConnectionString);
}
