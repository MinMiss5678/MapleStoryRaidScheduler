using Application.Interface;
using Microsoft.AspNetCore.Http;
using Moq;
using Presentation.WebApi.Middleware;
using Xunit;

namespace Test;

/// <summary>
/// IdempotencyMiddleware 單元測試。
/// 去重儲存以 <see cref="IIdempotencyStore"/> mock（Redis 實作另有整合測試）。
/// 釘住契約：非寫入放行；寫入缺 key / 非 UUID → 400；store 回 false（重複）→ 409；回 true（第一次）→ 放行。
/// </summary>
public class IdempotencyMiddlewareTests
{
    private static (IdempotencyMiddleware Middleware, Func<int> NextCalls) BuildMiddleware(IIdempotencyStore store)
    {
        var calls = 0;
        RequestDelegate next = _ => { calls++; return Task.CompletedTask; };
        return (new IdempotencyMiddleware(next, store), () => calls);
    }

    // store：TryMarkAsync 一律回指定值（true=第一次放行 / false=重複）
    private static Mock<IIdempotencyStore> StoreReturning(bool firstTime)
    {
        var mock = new Mock<IIdempotencyStore>();
        mock.Setup(s => s.TryMarkAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(firstTime);
        return mock;
    }

    private static DefaultHttpContext BuildContext(string method, string? idempotencyKey = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        if (idempotencyKey is not null)
            context.Request.Headers["X-Idempotency-Key"] = idempotencyKey;
        return context;
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task 非寫入方法_不查去重_直接放行(string method)
    {
        var store = StoreReturning(true);
        var (middleware, nextCalls) = BuildMiddleware(store.Object);

        await middleware.Invoke(BuildContext(method));

        Assert.Equal(1, nextCalls());
        store.Verify(s => s.TryMarkAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task 寫入方法_缺_key_回_400_且不查去重(string method)
    {
        var store = StoreReturning(true);
        var (middleware, nextCalls) = BuildMiddleware(store.Object);
        var context = BuildContext(method);

        await middleware.Invoke(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(0, nextCalls());
        store.Verify(s => s.TryMarkAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    [Fact]
    public async Task 寫入方法_key_非_UUID_回_400()
    {
        var (middleware, nextCalls) = BuildMiddleware(StoreReturning(true).Object);
        var context = BuildContext("POST", "not-a-uuid");

        await middleware.Invoke(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(0, nextCalls());
    }

    [Fact]
    public async Task 合法_key_第一次_放行()
    {
        var (middleware, nextCalls) = BuildMiddleware(StoreReturning(true).Object);
        var context = BuildContext("POST", Guid.NewGuid().ToString());

        await middleware.Invoke(context);

        Assert.Equal(1, nextCalls());
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task 合法_key_重複_回_409_且不進下一個()
    {
        // store 回 false = 此 key 已看過
        var (middleware, nextCalls) = BuildMiddleware(StoreReturning(false).Object);
        var context = BuildContext("POST", Guid.NewGuid().ToString());

        await middleware.Invoke(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal(0, nextCalls());
    }

    [Fact]
    public async Task api_internal_路徑_即使缺_key_也直接放行()
    {
        // 呼叫方是內部服務（Seq.App.HttpRequest），沒有前端 apiClient 那套 crypto.randomUUID() 機制。
        var store = StoreReturning(true);
        var (middleware, nextCalls) = BuildMiddleware(store.Object);
        var context = BuildContext("POST"); // 故意不帶 X-Idempotency-Key
        context.Request.Path = "/api/internal/alert-mail";

        await middleware.Invoke(context);

        Assert.Equal(1, nextCalls());
        store.Verify(s => s.TryMarkAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
    }
}
