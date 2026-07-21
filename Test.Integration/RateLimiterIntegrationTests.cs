using System.Net;
using Application.Interface;
using Application.Options;
using Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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
///
/// 覆寫走 services 層 PostConfigure（保證最後執行、贏過 app 的 config 繫結），
/// 不靠 ConfigureAppConfiguration 的來源優先序——那在 CI 會被 appsettings/env 蓋掉。
/// </summary>
public class RateLimiterIntegrationTests : IClassFixture<RateLimiterIntegrationTests.Factory>
{
    private const string TestSecret = "integration-test-secret-key-at-least-32-bytes-long!!";
    private const int TestPermitLimit = 5;

    // /api/Period/GetByNow：非 [AllowAnonymous]、無 role 限制 → user JWT 打得過 auth、進得了限流器
    private const string ProtectedPath = "/api/Period/GetByNow";

    private readonly Factory _factory;
    public RateLimiterIntegrationTests(Factory factory) => _factory = factory;

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        // 由我手動帶 Cookie header 送 jwtToken；關掉 client 的 CookieContainer，否則會蓋掉手動 header。
        HandleCookies = false,
        // 以 https base address 送 → UseHttpsRedirection 見已是 https 即放行，不在限流器前 307 轉走。
        BaseAddress = new Uri("https://localhost")
    });

    public class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development"); // 非 Production

            // services 層強制覆寫（跑在 app 註冊之後 → 贏過 Configure/Bind；不靠 config 來源優先序）
            // 註：IDbConnection 已改延遲開啟（見 Program.cs），auth 建構時不再 eager 開 DB → 不需再覆寫它，
            // 請求本來就到得了限流器（過限流的少數請求會在下游用到 DB 時才 500，已在限流器之後、無妨）。
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<RateLimitOptions>(o =>
                {
                    o.PermitLimit = TestPermitLimit; // 小上限 → 發遠超請求即穩定觸發 429
                    o.WindowSeconds = 10;
                });
                services.PostConfigure<JwtOptions>(o =>
                {
                    // CreateToken 與 middleware ValidateToken 共用這組（鑄與驗一致、且是已知值）
                    o.SecretKey = TestSecret;
                    o.Issuer = "test";
                    o.Audience = "test";
                });
            });
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

    // 上限 5/10s，一口氣發 60 次遠超上限。即使跨一個視窗邊界（<10s 最多 1 次邊界 → 至多放行 2×5=10），
    // 60 次也必定出現至少一個 429。不依賴「第幾次」。同時擷取首個回應 body 便於失敗時診斷。
    private async Task<(List<HttpStatusCode> statuses, string firstBody)> BurstAsync(HttpClient client, string token, int n)
    {
        var statuses = new List<HttpStatusCode>(n);
        var firstBody = "";
        for (int i = 0; i < n; i++)
        {
            var resp = await client.SendAsync(AuthedGet(token));
            statuses.Add(resp.StatusCode);
            if (i == 0) firstBody = await resp.Content.ReadAsStringAsync();
        }
        return (statuses, firstBody);
    }

    private static string Diagnose(List<HttpStatusCode> statuses, string firstBody)
    {
        var dist = string.Join(",", statuses.GroupBy(s => s).Select(g => $"{(int)g.Key}×{g.Count()}"));
        var body = firstBody.Length > 200 ? firstBody[..200] : firstBody;
        return $"狀態碼分布：{dist}；首個回應 body：{body}";
    }

    [Fact]
    public async Task ExceedsPerUserLimit_Returns429()
    {
        var client = CreateClient();

        var (statuses, firstBody) = await BurstAsync(client, CreateJwt(1001), 60);

        Assert.True(statuses.Contains(HttpStatusCode.TooManyRequests),
            $"預期出現 429。{Diagnose(statuses, firstBody)}");
    }

    [Fact]
    public async Task DifferentUsers_HaveIndependentLimits()
    {
        var client = CreateClient();

        // 使用者 A 打到被限
        var (aStatuses, aBody) = await BurstAsync(client, CreateJwt(2001), 60);
        Assert.True(aStatuses.Contains(HttpStatusCode.TooManyRequests),
            $"預期 A 出現 429。{Diagnose(aStatuses, aBody)}");

        // 使用者 B（不同 discordId）自己的視窗仍空 → 第一次就不該被限
        var respB = await client.SendAsync(AuthedGet(CreateJwt(2002)));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, respB.StatusCode);
    }
}
