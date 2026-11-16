using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Presentation.WebApi.Attributes;

namespace Presentation.WebApi.Middleware;

/// <summary>
/// 授權（只回 403）：比對 <see cref="AuthorizeRoleAttribute"/> 與 context.User 的角色。
/// 刻意放在 <see cref="AuthenticationMiddleware"/> 的下游——認證端任何成功路徑的 <c>await next()</c>
/// 呼叫的就是這裡，結構上無法跳過授權（0ed4125 → f0f32fb → 60faf7b 那三行的根治）。
/// </summary>
public class RoleAuthorizationMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() != null)
        {
            await next(context);
            return;
        }

        // 防禦性 fail-closed：認證端理應已擋下無身分的請求；萬一漏放，這裡也不放行
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // 類別與方法上的標記都要通過（AND）：方法只能收緊、不能放寬類別的限制
        var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
        var requirements = endpoint?.Metadata.GetOrderedMetadata<AuthorizeRoleAttribute>() ?? [];
        if (requirements.Any(r => !r.Roles.Contains(role)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }
}
