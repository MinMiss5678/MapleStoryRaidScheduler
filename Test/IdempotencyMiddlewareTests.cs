using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Presentation.WebApi.Middleware;
using Xunit;

namespace Test;

/// <summary>
/// IdempotencyMiddleware 單元測試。
/// 不經 WebApplicationFactory / DB —— middleware 只相依 RequestDelegate + IMemoryCache，
/// 且在管線中跑在 Auth / UnitOfWork 之前、400/409 會直接短路，故能純單元測、快又穩。
/// 釘住核心契約：非寫入方法放行；寫入缺 key / 非 UUID → 400；同一把 key 重送 → 409
/// （前端 CharacterForm 依賴這個 409 契約做「重複提交」的靜默處理）。
/// </summary>
public class IdempotencyMiddlewareTests
{
    // 每個測試給一份全新 cache（同一 mw 內共用，才驗得出「重送撞快取」）
    private static (IdempotencyMiddleware Middleware, Func<int> NextCalls) BuildMiddleware()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var calls = 0;
        RequestDelegate next = _ => { calls++; return Task.CompletedTask; };
        return (new IdempotencyMiddleware(next, cache), () => calls);
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
    public async Task 非寫入方法_不需要_key_直接放行(string method)
    {
        var (middleware, nextCalls) = BuildMiddleware();
        var context = BuildContext(method);

        await middleware.Invoke(context);

        Assert.Equal(1, nextCalls());
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task 寫入方法_缺_key_回_400_且不進下一個(string method)
    {
        var (middleware, nextCalls) = BuildMiddleware();
        var context = BuildContext(method);

        await middleware.Invoke(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(0, nextCalls());
    }

    [Fact]
    public async Task 寫入方法_key_非_UUID_回_400()
    {
        var (middleware, nextCalls) = BuildMiddleware();
        var context = BuildContext("POST", "not-a-uuid");

        await middleware.Invoke(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(0, nextCalls());
    }

    [Fact]
    public async Task 寫入方法_合法_key_第一次_放行()
    {
        var (middleware, nextCalls) = BuildMiddleware();
        var context = BuildContext("POST", Guid.NewGuid().ToString());

        await middleware.Invoke(context);

        Assert.Equal(1, nextCalls());
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task 同一把_key_重送_回_409_且不再進下一個()
    {
        var (middleware, nextCalls) = BuildMiddleware();
        var key = Guid.NewGuid().ToString();

        // 第一次：放行、進 next
        await middleware.Invoke(BuildContext("POST", key));
        Assert.Equal(1, nextCalls());

        // 同一把 key 重送：回 409、不再進 next（核心去重契約）
        var resend = BuildContext("POST", key);
        await middleware.Invoke(resend);

        Assert.Equal(StatusCodes.Status409Conflict, resend.Response.StatusCode);
        Assert.Equal(1, nextCalls()); // 仍是 1 → 第二次沒進 next
    }
}
