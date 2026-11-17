using System.Data;
using Infrastructure.Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Presentation.WebApi.Extensions;
using Presentation.WebApi.Http;
using Xunit;

namespace Test;

/// <summary>
/// G 組（plans/2026-11-16-auth-401-reason-and-commit-before-response.md §四）：交易中不准發對外 HTTP。
/// 把網路呼叫包進 DB 交易會讓連線在等外部服務時一直被佔住（例：登入時三次 Discord 呼叫）；
/// 對方一慢，連線池就會被耗盡。這道檢查讓這種寫法第一次執行就報錯。
/// </summary>
public class NoHttpCallInTransactionHandlerTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private static async Task<DbContext> DbContextAsync(bool inTransaction)
    {
        var conn = new Mock<IDbConnection>();
        conn.SetupGet(c => c.State).Returns(ConnectionState.Open);
        conn.Setup(c => c.BeginTransaction()).Returns(new Mock<IDbTransaction>().Object);
        var db = new DbContext(conn.Object);
        if (inTransaction)
            await db.BeginAsync();
        return db;
    }

    private static IHttpContextAccessor AccessorWith(DbContext? db)
    {
        if (db == null)
            return new HttpContextAccessor { HttpContext = null };
        var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        return new HttpContextAccessor { HttpContext = new DefaultHttpContext { RequestServices = services } };
    }

    private static HttpMessageInvoker Invoker(IHttpContextAccessor accessor, RecordingHandler inner) =>
        new(new NoHttpCallInTransactionHandler(accessor) { InnerHandler = inner });

    private static HttpRequestMessage Request() => new(HttpMethod.Get, "https://example.invalid/");

    [Fact]
    public async Task G1_有進行中的交易時發HTTP_丟例外且請求不送出()
    {
        var inner = new RecordingHandler();
        var invoker = Invoker(AccessorWith(await DbContextAsync(inTransaction: true)), inner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => invoker.SendAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Contains("交易", ex.Message);
        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    public async Task G2_沒有交易時_照常送出()
    {
        var inner = new RecordingHandler();
        var invoker = Invoker(AccessorWith(await DbContextAsync(inTransaction: false)), inner);

        var response = await invoker.SendAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task G3_不在HTTP請求內_照常送出()
    {
        var inner = new RecordingHandler();
        var invoker = Invoker(AccessorWith(null), inner);

        await invoker.SendAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task G4_所有經HttpClientFactory建立的HttpClient都掛上這道檢查()
    {
        var inner = new RecordingHandler();
        var accessor = AccessorWith(await DbContextAsync(inTransaction: true));
        var services = new ServiceCollection();
        services.AddSingleton(accessor);
        services.AddNoHttpCallInTransactionGuard(); // 正式環境 Program.cs 呼叫的同一個註冊
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => inner));
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        foreach (var client in new[] { factory.CreateClient(), factory.CreateClient("any-named-client") })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.GetAsync("https://example.invalid/", TestContext.Current.CancellationToken));
        }

        Assert.Equal(0, inner.Calls);
    }
}
