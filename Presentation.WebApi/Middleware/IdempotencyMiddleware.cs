using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Presentation.WebApi.Middleware;

/// <summary>
/// Idempotency Middleware:
/// - POST / PUT / DELETE must include a valid UUID X-Idempotency-Key header, else 400.
/// - Returns 409 Conflict if same key is seen within 60 seconds.
/// </summary>
public class IdempotencyMiddleware(RequestDelegate next, IMemoryCache cache)
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
        // 強制 UUID = 保證全域唯一且不可猜測，取代「key 綁使用者」的跨使用者碰撞/佔用防護
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

        var cacheKey = $"idempotency:{key}";
        if (cache.TryGetValue(cacheKey, out _))
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

        cache.Set(cacheKey, true, _ttl);
        await next(context);
    }
}
