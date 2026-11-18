using System.Collections.Concurrent;
using System.Net;
using System.Threading.RateLimiting;
using Application.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Presentation.WebApi.Attributes;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 交易邊界（plans/2026-11-16-auth-401-reason-and-commit-before-response.md C 組）：走真的 MVC 管線，
/// 以假 <see cref="IUnitOfWork"/> 記錄 Begin／Commit／Rollback 與「commit 當下回應是否已開始送出」。
/// 端點用測試專用的 <see cref="TxProbeController"/>，不碰 DB，只驗交易邊界本身。
/// </summary>
[Collection("webapp-host")]
public class TransactionBoundaryIntegrationTests : IClassFixture<TransactionBoundaryIntegrationTests.Factory>
{
    private readonly Factory _factory;

    public TransactionBoundaryIntegrationTests(Factory factory)
    {
        _factory = factory;
        _factory.Log.Reset();
    }

    // ---------------------------------------------------------------- 測試基礎設施

    public sealed class TxLog
    {
        public ConcurrentQueue<string> Events { get; } = new();
        public volatile bool FailCommit;

        public void Reset()
        {
            Events.Clear();
            FailCommit = false;
        }
    }

    private sealed class RecordingUnitOfWork(TxLog log, IHttpContextAccessor accessor) : IUnitOfWork
    {
        public Task BeginAsync()
        {
            log.Events.Enqueue("begin");
            return Task.CompletedTask;
        }

        public Task CommitAsync()
        {
            log.Events.Enqueue($"commit(started={accessor.HttpContext?.Response.HasStarted})");
            if (log.FailCommit)
                throw new InvalidOperationException("模擬 commit 失敗");
            return Task.CompletedTask;
        }

        public Task RollbackAsync()
        {
            log.Events.Enqueue("rollback");
            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysAllowIdempotencyStore : IIdempotencyStore
    {
        public Task<bool> TryMarkAsync(string key, TimeSpan ttl) => Task.FromResult(true);
        public Task RemoveAsync(string key) => Task.CompletedTask;
    }

    public class Factory : WebApplicationFactory<Program>
    {
        public TxLog Log { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.AddControllers().AddApplicationPart(typeof(TxProbeController).Assembly);
                services.AddSingleton(Log);
                services.AddScoped<IUnitOfWork, RecordingUnitOfWork>();
                // 寫入請求會經過冪等與限流；兩者都與交易邊界無關 → 換成永遠放行，不依賴 Redis
                services.AddSingleton<IIdempotencyStore, AlwaysAllowIdempotencyStore>();
                services.Configure<RateLimiterOptions>(o =>
                    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                        RateLimitPartition.GetNoLimiter("test")));
            });
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private string[] Events => _factory.Log.Events.ToArray();

    // ---------------------------------------------------------------- C 組

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task C1_寫入成功_commit時回應尚未開始送出(string method)
    {
        var response = await SendAsync(new HttpMethod(method), "/api/tx-probe/ok");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["begin", "commit(started=False)"], Events);
    }

    [Fact]
    public async Task C2_commit失敗_用戶端收到500而不是200()
    {
        _factory.Log.FailCommit = true;

        var response = await SendAsync(HttpMethod.Post, "/api/tx-probe/ok");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task C3_寫入回4xx_rollback_不commit()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/tx-probe/bad");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["begin", "rollback"], Events);
    }

    [Fact]
    public async Task C3_寫入丟例外_rollback_不commit_用戶端收到500()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/tx-probe/throw");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(["begin", "rollback"], Events);
    }

    [Fact]
    public async Task C4_讀取請求_不開交易()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/tx-probe/ok");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Events);
    }

    [Fact]
    public async Task C5_commit成功但寫回應失敗_資料已提交_用戶端收到5xx()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/tx-probe/unserializable");

        Assert.True((int)response.StatusCode >= 500, $"實際 {(int)response.StatusCode}");
        Assert.Equal(["begin", "commit(started=False)"], Events);
    }

    [Fact]
    public async Task C6_標記NoAmbientTransaction的寫入端點_不開交易()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/tx-probe/no-ambient");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Events);
    }

    [Fact]
    public void C6_登入端點_自己管交易_標記NoAmbientTransaction()
    {
        var login = typeof(Presentation.WebApi.Controller.AuthController).GetMethod("LoginAsync");

        Assert.NotNull(login);
        Assert.NotNull(
            login.GetCustomAttributes(typeof(NoAmbientTransactionAttribute), inherit: true).SingleOrDefault());
    }
}

/// <summary>測試專用端點：只回固定結果，不碰 DB。</summary>
[ApiController]
[AllowAnonymous]
[Route("api/tx-probe")]
public class TxProbeController : ControllerBase
{
    [HttpGet("ok")]
    [HttpPost("ok")]
    [HttpPut("ok")]
    [HttpPatch("ok")]
    [HttpDelete("ok")]
    public IActionResult Ok200() => Ok(new { ok = true });

    [HttpPost("bad")]
    public IActionResult Bad() => BadRequest(new { error = "bad" });

    [HttpPost("throw")]
    public IActionResult Throw() => throw new InvalidOperationException("模擬 action 失敗");

    [HttpPost("unserializable")]
    public IActionResult Unserializable() => Ok(new BrokenPayload());

    [HttpPost("no-ambient")]
    [NoAmbientTransaction]
    public IActionResult NoAmbient() => Ok(new { ok = true });

    public sealed class BrokenPayload
    {
        public string Value => throw new InvalidOperationException("模擬序列化失敗");
    }
}
