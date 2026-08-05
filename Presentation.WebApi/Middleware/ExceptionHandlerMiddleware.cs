using Application.Exceptions;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Presentation.WebApi.Middleware;

/// <summary>
/// 全域例外處理 Middleware：
/// - AppException 子類 → 對應 4xx + 標準 ProblemDetails
/// - 其他例外 → 500 + 隱藏內部細節（非開發環境）
/// </summary>
public class ExceptionHandlerMiddleware(RequestDelegate next, ILogger<ExceptionHandlerMiddleware> logger, IHostEnvironment env)
{
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var (status, title) = ex switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
            BusinessException => (StatusCodes.Status400BadRequest, "Bad Request"),
            DomainException => (StatusCodes.Status400BadRequest, "Bad Request"),   // 領域不變式違反
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            AppException => (StatusCodes.Status400BadRequest, "Bad Request"),
            // DB 後防：unique 違反多為並發 race（idempotency、同時報名、重複申請/邀請）→ 預期、非 bug → 409。
            // 其餘 DB 約束違反（FK/check/not-null/length）刻意不轉，落 _ → 500 + 告警，保留「app 漏驗」訊號。
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
                => (StatusCodes.Status409Conflict, "Conflict"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        // 5xx 才記 Error；4xx 記 Warning 即可
        if (status >= 500)
            logger.LogError(ex, "Unhandled exception");
        else
            logger.LogWarning(ex, "Handled exception: {Message}", ex.Message);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            // 非開發環境隱藏 5xx 內部細節；DB 例外一律不回原訊息（避免洩漏 constraint/欄位名）
            Detail = ex is PostgresException
                ? (status < 500 ? "發生併發衝突，請稍後重試。" : null)
                : (status < 500 || env.IsDevelopment()) ? ex.Message : null,
        };

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
