using Application.Exceptions;
using Microsoft.AspNetCore.Mvc;

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
            NotFoundException  => (StatusCodes.Status404NotFound,          "Not Found"),
            BusinessException  => (StatusCodes.Status400BadRequest,        "Bad Request"),
            ForbiddenException => (StatusCodes.Status403Forbidden,         "Forbidden"),
            AppException       => (StatusCodes.Status400BadRequest,        "Bad Request"),
            _                  => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        // 5xx 才記 Error；4xx 記 Warning 即可
        if (status >= 500)
            logger.LogError(ex, "Unhandled exception");
        else
            logger.LogWarning(ex, "Business exception: {Message}", ex.Message);

        var problem = new ProblemDetails
        {
            Status = status,
            Title  = title,
            // 非開發環境隱藏 5xx 內部細節
            Detail = (status < 500 || env.IsDevelopment()) ? ex.Message : null,
        };

        context.Response.StatusCode  = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
