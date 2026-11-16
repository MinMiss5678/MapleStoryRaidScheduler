namespace Presentation.WebApi.Attributes;

/// <summary>
/// 端點要求的角色（符合其中一個即可）。至少要帶一個角色：<c>[AuthorizeRole()]</c> 編譯不過，
/// 避免「看似有權限控管、實際放行所有已登入者」。同一目標不可疊用（疊用時以前只會讀到其中一個，另一個默默失效）；
/// 類別與方法各標一個時兩層都要通過，見 <see cref="Presentation.WebApi.Middleware.RoleAuthorizationMiddleware"/>。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class AuthorizeRoleAttribute(string role, params string[] moreRoles) : Attribute
{
    public string[] Roles { get; } = [role, .. moreRoles];
}
