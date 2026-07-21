using System.Net;
using Application.Interface;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 驗「登入後按身分（discordId）限流」：同一使用者超過視窗上限 → 429；不同使用者各自獨立。
///
/// 刻意不需要 DB：
///   - JWT 驗證是無狀態的（JwtService 只依賴 JwtOptions），可直接鑄 token 過 auth。
///   - 限流 middleware 在 UnitOfWork（開 DB 交易）之前就攔，被擋的請求根本不碰 DB。
///   - 未超上限的請求會因連不到 DB 回 500，但那不影響「有沒有被限流（429）」——限流計數在 UoW 之前。
/// 所以斷言只看「是不是 429」，與下游 DB 狀態無關。
/// </summary>
public class RateLimiterIntegrationTests : IClassFixture<RateLimiterIntegrationTests.Factory>
{
    // /api/Period/GetByNow：非 [AllowAnonymous]、無 role 限制 → user JWT 打得過 auth、進得了限流器
    private const string ProtectedPath = "/api/Period/GetByNow";

    private readonly Factory _factory;
    public RateLimiterIntegrationTests(Factory factory) => _factory = factory;

    // 以 https base address 送 → app 的 UseHttpsRedirection 見已是 https 即放行，
    // 不會在限流器之前把請求 307 轉走（CI 上會有 https port 導致轉址）。
    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });

    public class Factory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development"); // 非 Production
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // 無狀態 JWT：CreateToken 與 middleware ValidateToken 共用這組設定
                    ["Jwt:SecretKey"] = "integration-test-secret-key-at-least-32-bytes-long!!",
                    ["Jwt:Issuer"] = "test",
                    ["Jwt:Audience"] = "test",
                    // 測試用小上限：5 次/10 秒 → 發遠超的請求即可穩定觸發限流（不依賴時序）
                    ["RateLimit:PermitLimit"] = "5",
                    ["RateLimit:WindowSeconds"] = "10",
                    // 指向 127.0.0.1:1 → conn.Open() 立即被拒（fail fast）；限流在 UoW 前計數，不受影響
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1;Command Timeout=1",
                    ["ConnectionStrings:DefaultConnectionFile"] = ""
                });
            });
            return base.CreateHost(builder);
        }
    }

    private string CreateJwt(ulong discordId)
    {
        using var scope = _factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtService>();
        return jwt.CreateToken(new DiscordUser { Id = discordId, Name = "rl" }, "user", 60);
    }

    private static HttpRequestMessage AuthedGet(string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, ProtectedPath);
        req.Headers.Add("Cookie", $"jwtToken={token}");
        return req;
    }

    // 上限 5/10s，一口氣發 60 次遠超上限。即使跨一個視窗邊界（本機/CI <10s，最多 1 次邊界 →
    // 至多放行 2×5=10）、或少數請求未計數，60 次也必定出現至少一個 429。不依賴「第幾次」。
    private async Task<List<HttpStatusCode>> BurstAsync(HttpClient client, string token, int n)
    {
        var statuses = new List<HttpStatusCode>(n);
        for (int i = 0; i < n; i++)
            statuses.Add((await client.SendAsync(AuthedGet(token))).StatusCode);
        return statuses;
    }

    [Fact]
    public async Task ExceedsPerUserLimit_Returns429()
    {
        var client = CreateClient();

        var statuses = await BurstAsync(client, CreateJwt(1001), 60);

        // 同一 discordId 遠超上限 → 至少一個被限（附上實際狀態碼便於診斷）
        Assert.True(statuses.Contains(HttpStatusCode.TooManyRequests),
            $"預期出現 429，實際狀態碼分布：{string.Join(",", statuses.GroupBy(s => s).Select(g => $"{(int)g.Key}×{g.Count()}"))}");
    }

    [Fact]
    public async Task DifferentUsers_HaveIndependentLimits()
    {
        var client = CreateClient();

        // 使用者 A 打到被限
        var aStatuses = await BurstAsync(client, CreateJwt(2001), 60);
        Assert.True(aStatuses.Contains(HttpStatusCode.TooManyRequests),
            $"預期 A 出現 429，實際：{string.Join(",", aStatuses.GroupBy(s => s).Select(g => $"{(int)g.Key}×{g.Count()}"))}");

        // 使用者 B（不同 discordId）自己的視窗仍空 → 第一次就不該被限
        var respB = await client.SendAsync(AuthedGet(CreateJwt(2002)));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, respB.StatusCode);
    }
}
