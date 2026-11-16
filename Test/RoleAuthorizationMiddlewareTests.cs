using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Presentation.WebApi.Attributes;
using Presentation.WebApi.Middleware;
using Xunit;

namespace Test;

/// <summary>
/// RoleAuthorizationMiddleware 單元測試（plans/2026-11-04-split-authentication-authorization.md §四 A 組、D3／D4）。
/// 授權從認證 middleware 拆到下游：認證只負責建立身分（401），授權只負責比對角色（403）。
/// </summary>
public class RoleAuthorizationMiddlewareTests
{
    private static ClaimsPrincipal Authenticated(string? role) =>
        new(new ClaimsIdentity(
            role == null
                ? [new Claim("discordId", "123")]
                : [new Claim("discordId", "123"), new Claim(ClaimTypes.Role, role)],
            "jwt"));

    private static DefaultHttpContext BuildContext(ClaimsPrincipal? user, params object[] endpointMetadata)
    {
        var context = new DefaultHttpContext();
        if (user != null)
            context.User = user;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(endpointMetadata), "test"));
        return context;
    }

    private static async Task<(bool NextCalled, int Status)> Run(DefaultHttpContext context)
    {
        var nextCalled = false;
        await new RoleAuthorizationMiddleware().InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        return (nextCalled, context.Response.StatusCode);
    }

    [Fact]
    public async Task A1_端點無角色要求_有身分_放行()
    {
        var (nextCalled, status) = await Run(BuildContext(Authenticated("user")));

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task A2_要求admin_角色是admin_放行()
    {
        var (nextCalled, _) = await Run(BuildContext(Authenticated("admin"), new AuthorizeRoleAttribute("admin")));

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task A3_要求admin_角色是user_回403且不往下執行()
    {
        var (nextCalled, status) = await Run(BuildContext(Authenticated("user"), new AuthorizeRoleAttribute("admin")));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task A4_要求admin_角色為空字串_回403()
    {
        var (nextCalled, status) = await Run(BuildContext(Authenticated(""), new AuthorizeRoleAttribute("admin")));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task A5_要求admin_完全沒有角色claim_回403()
    {
        var (nextCalled, status) = await Run(BuildContext(Authenticated(null), new AuthorizeRoleAttribute("admin")));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task A6_AllowAnonymous_無身分_放行()
    {
        var (nextCalled, status) = await Run(BuildContext(null, new AllowAnonymousAttribute()));

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task A7_AllowAnonymous_同時要求admin_AllowAnonymous優先_放行()
    {
        var (nextCalled, _) =
            await Run(BuildContext(null, new AllowAnonymousAttribute(), new AuthorizeRoleAttribute("admin")));

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task A8_沒有任何身分_要求admin_回401_不得放行()
    {
        var (nextCalled, status) = await Run(BuildContext(null, new AuthorizeRoleAttribute("admin")));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }

    [Fact]
    public async Task A10_要求多個角色之一_符合其中一個_放行()
    {
        var (nextCalled, _) =
            await Run(BuildContext(Authenticated("leader"), new AuthorizeRoleAttribute("admin", "leader")));

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task A11_類別要求admin_方法放寬到leader_角色是leader_回403()
    {
        // endpoint metadata 的順序：類別上的標記在前、方法上的在後；兩層都要通過，方法不能放寬類別的限制
        var (nextCalled, status) = await Run(BuildContext(Authenticated("leader"),
            new AuthorizeRoleAttribute("admin"), new AuthorizeRoleAttribute("admin", "leader")));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public void D3_AuthorizeRole_至少要帶一個角色_無參數寫法建不出來()
    {
        var ctors = typeof(AuthorizeRoleAttribute).GetConstructors();

        Assert.NotEmpty(ctors);
        Assert.All(ctors, c => Assert.Contains(c.GetParameters(),
            p => p.GetCustomAttribute<ParamArrayAttribute>() == null && !p.IsOptional));
    }

    [Fact]
    public void D4_AuthorizeRole_同一目標不可疊用()
    {
        var usage = typeof(AuthorizeRoleAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.False(usage.AllowMultiple);
    }
}
