using Application.Interface;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Presentation.WebApi.Attributes;

namespace Presentation.WebApi.Filters;

/// <summary>
/// 一個寫入請求 = 一個交易（全域 action filter）：action 執行前開交易，action 執行後、<b>結果寫出前</b>依結果 commit／rollback。
/// <para>
/// 為什麼不用 middleware：middleware 包在 MVC 外面，<c>await next</c> 回來時回應通常已經開始送出——
/// commit 晚於回應，commit 失敗時用戶端已收到 200（plans/2026-11-16-auth-401-reason-and-commit-before-response.md C 組）。
/// 放在 action filter，結構上保證 commit 早於寫回應；commit 失敗或寫回應失敗都還能回 5xx。
/// </para>
/// 規則：
/// - 只處理寫入方法（POST／PUT／PATCH／DELETE）；讀取不開交易。
/// - 標了 <see cref="NoAmbientTransactionAttribute"/> 的端點自己管交易（例：登入要先呼叫 Discord）。
/// - action 丟例外（且沒被處理）或結果狀態碼 ≥ 400 → rollback；否則 commit。沒有狀態碼的結果（例：直接回物件）視為 200。
/// - commit 成功但之後寫回應失敗：資料已提交、用戶端收到 5xx（取捨：比「收到 200 但資料沒存」好；重試由冪等／DB 約束擋重複）。
/// </summary>
public sealed class UnitOfWorkActionFilter(IUnitOfWork unitOfWork) : IAsyncActionFilter
{
    private static readonly HashSet<string> WriteMethods = ["POST", "PUT", "PATCH", "DELETE"];

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!WriteMethods.Contains(context.HttpContext.Request.Method) ||
            context.ActionDescriptor.EndpointMetadata.OfType<NoAmbientTransactionAttribute>().Any())
        {
            await next();
            return;
        }

        await unitOfWork.BeginAsync();

        ActionExecutedContext executed;
        try
        {
            executed = await next();
        }
        catch
        {
            await unitOfWork.RollbackAsync();
            throw;
        }

        var failed = (executed.Exception != null && !executed.ExceptionHandled) ||
                     StatusCodeOf(executed.Result) >= StatusCodes.Status400BadRequest;
        if (failed)
            await unitOfWork.RollbackAsync();
        else
            await unitOfWork.CommitAsync();
    }

    private static int StatusCodeOf(IActionResult? result) =>
        result is IStatusCodeActionResult { StatusCode: { } code } ? code : StatusCodes.Status200OK;
}
