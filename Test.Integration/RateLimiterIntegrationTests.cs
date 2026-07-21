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

    [Fact]
    public async Task ExceedsPerUserLimit_Returns429()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = CreateJwt(1001);

        // 前 100 次（PermitLimit）：可能因無 DB 回 500，但都不該是 429
        for (int i = 0; i < 100; i++)
        {
            var resp = await client.SendAsync(AuthedGet(token));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, resp.StatusCode);
        }

        // 第 101 次：同一 discordId 超過 100/10s → 429
        var limited = await client.SendAsync(AuthedGet(token));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task DifferentUsers_HaveIndependentLimits()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // 用光使用者 A 的額度（101 次 → 最後幾次被限）
        var tokenA = CreateJwt(2001);
        for (int i = 0; i < 101; i++)
            await client.SendAsync(AuthedGet(tokenA));

        // 使用者 B（不同 discordId）自己的視窗仍是滿的 → 不該是 429
        var respB = await client.SendAsync(AuthedGet(CreateJwt(2002)));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, respB.StatusCode);
    }
}
