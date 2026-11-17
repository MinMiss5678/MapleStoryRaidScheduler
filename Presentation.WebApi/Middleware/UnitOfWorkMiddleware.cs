using Application.Interface;
using Presentation.WebApi.Attributes;

namespace Presentation.WebApi.Middleware;

public class UnitOfWorkMiddleware
{
    private readonly RequestDelegate _next;

    public UnitOfWorkMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, IUnitOfWork uow)
    {
        // 只針對「會修改資料」的請求
        if (!IsWriteRequest(context))
        {
            await _next(context);
            return;
        }

        // 自己管交易的端點（例：登入要先完成 Discord 呼叫，才開交易寫 DB）
        if (context.GetEndpoint()?.Metadata.GetMetadata<NoAmbientTransactionAttribute>() != null)
        {
            await _next(context);
            return;
        }

        await uow.BeginAsync();

        try
        {
            await _next(context);

            // 如果 HTTP status 是成功才 commit
            if (context.Response.StatusCode < 400)
            {
                await uow.CommitAsync();
            }
            else
            {
                await uow.RollbackAsync();
            }
        }
        catch
        {
            await uow.RollbackAsync();
            throw;
        }
    }

    private bool IsWriteRequest(HttpContext context)
    {
        return context.Request.Method switch
        {
            "POST" => true,
            "PUT" => true,
            "PATCH" => true,
            "DELETE" => true,
            _ => false
        };
    }
}
