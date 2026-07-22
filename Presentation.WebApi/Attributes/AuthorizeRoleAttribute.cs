namespace Presentation.WebApi.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class AuthorizeRoleAttribute(params string[] roles) : Attribute
{
    public string[] Roles { get; } = roles;
}
