using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Application.Interface;
using Application.Options;
using Domain.Entities;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Infrastructure.Redis;
using Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Presentation.WebApi.RateLimiting;
using StackExchange.Redis;
using Xunit;

namespace Test.Integration;

/// <summary>
/// Redis 不可用時的 fail-open（plans/2026-11-16-redis-fail-open.md §三 F／G／H／R 組）。
/// 三個元件（冪等、session 快取、限流）Redis 掛掉時都要「放行」，而且要**很快**放行——
/// 預設設定下指令會排隊等到 5 秒逾時才失敗，一個請求最壞要等十幾秒。
/// 一律使用正式環境同一套連線設定（<see cref="RedisConnectionOptions.Build"/>）。
/// </summary>
[Collection("webapp-host")]
public class RedisFailOpenIntegrationTests
{
    private const string Unreachable = "127.0.0.1:1"; // 沒有服務的 port
    private static readonly TimeSpan PerOperationLimit = TimeSpan.FromSeconds(1);

    private static IConnectionMultiplexer ConnectUnreachable() =>
        ConnectionMultiplexer.Connect(RedisConnectionOptions.Build(Unreachable));

    private static async Task<(T Result, TimeSpan Elapsed)> Timed<T>(Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();
        var result = await action();
        return (result, sw.Elapsed);
    }

    private static async Task<TimeSpan> Timed(Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        await action();
        return sw.Elapsed;
    }

    private static Session SampleSession() => new()
    {
        SessionId = "sid",
        DiscordId = 1,
        SessionExpiry = DateTimeOffset.UtcNow.AddDays(1)
    };

    // ---------------------------------------------------------------- F 組：三個元件

    [Fact]
    public async Task F1_冪等store_Redis不可用_放行且1秒內回應()
    {
        await using var redis = ConnectUnreachable();
        var store = new RedisIdempotencyStore(redis, NullLogger<RedisIdempotencyStore>.Instance);

        var (allowed, elapsed) = await Timed(() => store.TryMarkAsync($"k:{Guid.NewGuid()}", TimeSpan.FromMinutes(1)));

        Assert.True(allowed);
        Assert.True(elapsed < PerOperationLimit, $"花了 {elapsed.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public async Task F5_冪等store撤銷_Redis不可用_不丟例外且1秒內回應()
    {
        await using var redis = ConnectUnreachable();
        var store = new RedisIdempotencyStore(redis, NullLogger<RedisIdempotencyStore>.Instance);

        var elapsed = await Timed(() => store.RemoveAsync($"k:{Guid.NewGuid()}"));

        Assert.True(elapsed < PerOperationLimit, $"花了 {elapsed.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public async Task F2_session快取_Redis不可用_當作沒命中_寫入刪除不丟例外_各1秒內()
    {
        await using var redis = ConnectUnreachable();
        var cache = new RedisSessionCache(redis, NullLogger<RedisSessionCache>.Instance);

        var (got, getElapsed) = await Timed(() => cache.GetAsync("1"));
        var setElapsed = await Timed(() => cache.SetAsync("1", SampleSession(), TimeSpan.FromMinutes(1)));
        var removeElapsed = await Timed(() => cache.RemoveAsync("1"));

        Assert.Null(got);
        Assert.True(getElapsed < PerOperationLimit, $"Get 花了 {getElapsed.TotalMilliseconds:F0} ms");
        Assert.True(setElapsed < PerOperationLimit, $"Set 花了 {setElapsed.TotalMilliseconds:F0} ms");
        Assert.True(removeElapsed < PerOperationLimit, $"Remove 花了 {removeElapsed.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public async Task F3_限流器_Redis不可用_放行且1秒內回應()
    {
        await using var redis = ConnectUnreachable();
        var limiter = new RedisFixedWindowRateLimiter(redis, NullLogger.Instance, $"rl:{Guid.NewGuid()}", 1,
            TimeSpan.FromMinutes(1));

        var (lease, elapsed) =
            await Timed(async () => await limiter.AcquireAsync(1, TestContext.Current.CancellationToken));

        Assert.True(lease.IsAcquired);
        Assert.True(elapsed < PerOperationLimit, $"花了 {elapsed.TotalMilliseconds:F0} ms");
    }

    // ---------------------------------------------------------------- G 組：整條管線

    public class UnreachableRedisFactory : WebApplicationFactory<Program>
    {
        public const string TestSecret = "integration-test-secret-key-at-least-32-bytes-long!!";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                // 正式的三個 Redis 元件都留著，只把連線換成指向沒有服務的 port（同一套連線設定）
                services.AddSingleton<IConnectionMultiplexer>(_ => ConnectUnreachable());
                // 寫入方法會經過 UnitOfWorkMiddleware 開 DB 交易；本測試沒有 DB → 換成 no-op，讓請求走到路由（404）
                services.AddScoped<IUnitOfWork, NoOpUnitOfWork>();
                services.PostConfigure<JwtOptions>(o =>
                {
                    o.SecretKey = TestSecret;
                    o.Issuer = "test";
                    o.Audience = "test";
                });
            });
        }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task BeginAsync() => Task.CompletedTask;
        public Task CommitAsync() => Task.CompletedTask;
        public Task RollbackAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task G1_真實管線_Redis不可用_寫入請求經過限流與冪等_2秒內回應()
    {
        await using var factory = new UnreachableRedisFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost")
        });
        string token;
        using (var scope = factory.Services.CreateScope())
            token = scope.ServiceProvider.GetRequiredService<IJwtService>()
                .CreateToken(new DiscordUser { Id = 4001, Name = "failopen" }, "user", 60);

        // 不存在的路由：一路經過 冪等（寫入方法）→ 認證 → 授權 → 限流 → 最後 404，不碰 DB
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/redis-fail-open-probe");
        request.Headers.Add("Cookie", $"jwtToken={token}");
        request.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());

        var (response, elapsed) = await Timed(() => client.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // 沒被限流（429）、沒被當成重複（409）
        Assert.True(elapsed < TimeSpan.FromSeconds(2), $"花了 {elapsed.TotalMilliseconds:F0} ms");
    }

    // ---------------------------------------------------------------- H 組：連線設定

    [Fact]
    public void H1_正式連線設定_FailFast且逾時短_不因連不上擋啟動()
    {
        var options = RedisConnectionOptions.Build("redis:6379");

        Assert.Equal(BacklogPolicy.FailFast, options.BacklogPolicy);
        Assert.True(options.AsyncTimeout <= 500, $"AsyncTimeout = {options.AsyncTimeout}");
        Assert.True(options.SyncTimeout <= 500, $"SyncTimeout = {options.SyncTimeout}");
        Assert.False(options.AbortOnConnectFail);
    }

    // ---------------------------------------------------------------- R 組：恢復後自動重連

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>主機 port 固定綁定：預設的隨機 port 在容器停／啟後會換，連線端就永遠連不回去，測不到重連。</summary>
    private static IContainer FixedPortRedis(int port) => new ContainerBuilder("redis:7-alpine")
        .WithPortBinding(port, 6379)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping"))
        .Build();

    /// <summary>在時限內重試，直到 check 成立；回傳是否成立。</summary>
    private static async Task<bool> EventuallyAsync(Func<Task<bool>> check, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await check())
                return true;
            await Task.Delay(500);
        }

        return false;
    }

    [Fact]
    public async Task R1_R2_Redis停止期間fail_open_重新啟動後30秒內自動恢復_且不留後遺症()
    {
        var port = FreePort();
        await using var container = FixedPortRedis(port);
        await container.StartAsync(TestContext.Current.CancellationToken);
        await using var redis = ConnectionMultiplexer.Connect(RedisConnectionOptions.Build($"127.0.0.1:{port}"));
        var store = new RedisIdempotencyStore(redis, NullLogger<RedisIdempotencyStore>.Instance);
        var cache = new RedisSessionCache(redis, NullLogger<RedisSessionCache>.Instance);

        RedisFixedWindowRateLimiter Limiter() =>
            new(redis, NullLogger.Instance, $"rl:{Guid.NewGuid()}", 1, TimeSpan.FromMinutes(1));

        // 正常時：同一 key 第二次被擋（確認真的連上了）
        var warmKey = $"k:{Guid.NewGuid()}";
        Assert.True(await store.TryMarkAsync(warmKey, TimeSpan.FromMinutes(1)));
        Assert.False(await store.TryMarkAsync(warmKey, TimeSpan.FromMinutes(1)));

        // 停止：三個元件 fail-open，各自 1 秒內
        await container.StopAsync(TestContext.Current.CancellationToken);
        var outageKey = $"k:{Guid.NewGuid()}";
        var (allowed, e1) = await Timed(() => store.TryMarkAsync(outageKey, TimeSpan.FromMinutes(1)));
        var (got, e2) = await Timed(() => cache.GetAsync("1"));
        var (lease, e3) =
            await Timed(async () => await Limiter().AcquireAsync(1, TestContext.Current.CancellationToken));
        Assert.True(allowed);
        Assert.Null(got);
        Assert.True(lease.IsAcquired);
        Assert.True(e1 < PerOperationLimit && e2 < PerOperationLimit && e3 < PerOperationLimit,
            $"冪等 {e1.TotalMilliseconds:F0} ms、快取 {e2.TotalMilliseconds:F0} ms、限流 {e3.TotalMilliseconds:F0} ms");

        // 重新啟動：30 秒內自動恢復正常行為
        await container.StartAsync(TestContext.Current.CancellationToken);
        var recovered = await EventuallyAsync(async () =>
        {
            var key = $"k:{Guid.NewGuid()}";
            return await store.TryMarkAsync(key, TimeSpan.FromMinutes(1))
                   && !await store.TryMarkAsync(key, TimeSpan.FromMinutes(1));
        }, TimeSpan.FromSeconds(30));
        Assert.True(recovered, "Redis 重新啟動後 30 秒內沒有恢復");

        // R2：恢復後不留後遺症——停止期間 fail-open 過的 key、新的 session、新的限流計數都照常運作
        Assert.True(await store.TryMarkAsync(outageKey, TimeSpan.FromMinutes(1))); // 停止期間沒真的寫入 → 現在是第一次
        Assert.False(await store.TryMarkAsync(outageKey, TimeSpan.FromMinutes(1)));
        await cache.SetAsync("2", SampleSession(), TimeSpan.FromMinutes(1));
        Assert.NotNull(await cache.GetAsync("2"));
        var limiter = Limiter();
        Assert.True((await limiter.AcquireAsync(1, TestContext.Current.CancellationToken)).IsAcquired);
        Assert.False((await limiter.AcquireAsync(1, TestContext.Current.CancellationToken)).IsAcquired);
    }
}
