using System.Data;
using System.Net;
using Infrastructure.Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Test.Integration;

/// <summary>
/// G4（正式接線）：Program.cs 真的把「交易中不准發對外 HTTP」掛到 DI 提供的 HttpClient 上
/// （DiscordOAuthClient 等就是注入這個 HttpClient）。單元測試只驗得到註冊方法本身，驗不到 Program 有沒有呼叫它。
/// </summary>
[Collection("webapp-host")]
public class TransactionGuardWiringIntegrationTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class Factory(StubHandler stub) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                // 不碰真 DB：DbContext 改用假連線（BeginAsync 走同步 BeginTransaction 分支）
                services.AddScoped(_ =>
                {
                    var conn = new Mock<IDbConnection>();
                    conn.SetupGet(c => c.State).Returns(ConnectionState.Open);
                    conn.Setup(c => c.BeginTransaction()).Returns(new Mock<IDbTransaction>().Object);
                    return new DbContext(conn.Object);
                });
                // 不打真網路：所有 HttpClient 的最底層換成假 handler
                services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => stub));
            });
        }
    }

    [Fact]
    public async Task G4_正式接線_注入的HttpClient在交易中發請求_被擋下()
    {
        var stub = new StubHandler();
        await using var factory = new Factory(stub);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbContext>();
        await db.BeginAsync();
        factory.Services.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        var client = scope.ServiceProvider.GetRequiredService<HttpClient>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetAsync("https://discord.com/api/users/@me", TestContext.Current.CancellationToken));
        Assert.Equal(0, stub.Calls);
    }
}
