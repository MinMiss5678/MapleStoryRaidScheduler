using Application.Interface;
using Microsoft.AspNetCore.Mvc;

namespace Presentation.WebApi.Middleware;

/// <summary>
/// 重複提交防護 Middleware：
/// - POST / PUT / DELETE must include a valid UUID X-Idempotency-Key header, else 400.
/// - Returns 409 Conflict if same key is seen within 60 seconds.
/// 去重狀態走 <see cref="IIdempotencyStore"/>（Redis 實作，跨 pod 共享）。
/// </summary>
public class IdempotencyMiddleware(RequestDelegate next, IIdempotencyStore store)
{
    private static readonly HashSet<string> _methods = ["POST", "PUT", "DELETE"];
    private static readonly TimeSpan _ttl = TimeSpan.FromSeconds(60);

    public async Task Invoke(HttpContext context)
    {
        if (!_methods.Contains(context.Request.Method))
        {
            await next(context);
            return;
        }

        var key = context.Request.Headers["X-Idempotency-Key"].FirstOrDefault();
        // 必須帶 X-Idempotency-Key，且必須是合法 UUID（前端用 crypto.randomUUID() 產生 v4）
        if (string.IsNullOrEmpty(key) || !Guid.TryParse(key, out _))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = "X-Idempotency-Key header 為必填，且必須是合法的 UUID。"
            });
            return;
        }

        // 第一次看到此 key → 放行；ttl 內重複 → 409。去重在 Redis（跨 pod）；Redis 掛則 store 採 fail-open 放行。
        if (!await store.TryMarkAsync(key, _ttl))
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = "此操作已提交，請勿重複送出。"
            });
            return;
        }

        await next(context);
    }
}
