using Application.Interface;

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
        
        if (context.Items.ContainsKey("SkipUow"))
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