using Application.Interface;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Moq;
using Presentation.WebApi.Attributes;
using Presentation.WebApi.Middleware;
using Xunit;

namespace Test;

/// <summary>
/// AuthenticationMiddleware 單元測試：釘住核心安全分支——
/// AllowAnonymous 放行、無憑證 401、session 查無 403、角色不符 403、有效 session/JWT 帶身分放行。
/// mock 四個服務，不碰 DB。
/// 刻意未涵蓋：JWT 過期→RefreshToken 那段（分支排列多、工大值低），另以整合測試較合適。
/// </summary>
public class AuthenticationMiddlewareTests
{
    private readonly Mock<ISessionService> _session = new();
    private readonly Mock<IPlayerService> _player = new();
    private readonly Mock<IJwtService> _jwt = new();
    private readonly Mock<IAuthService> _auth = new();

    public AuthenticationMiddlewareTests()
    {
        // 預設 JWT 驗證失敗（沒帶或無效）；需要有效 JWT 的測試再覆寫特定 token
        _jwt.Setup(j => j.ValidateToken(It.IsAny<string>()))
            .Returns(new JwtValidationResult { IsValid = false });
    }

    private AuthenticationMiddleware Build() => new(_session.Object, _player.Object, _jwt.Object, _auth.Object);

    private static DefaultHttpContext BuildContext(string? cookieHeader = null, params object[] endpointMetadata)
    {
        var context = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(cookieHeader))
            context.Request.Headers["Cookie"] = cookieHeader;
        if (endpointMetadata.Length > 0)
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
                new EndpointMetadataCollection(endpointMetadata), "test"));
        return context;
    }

    // 執行 middleware，回傳 (是否呼叫 next, context)
    private async Task<(bool NextCalled, DefaultHttpContext Context)> Run(DefaultHttpContext context)
    {
        var nextCalled = false;
        await Build().InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });
        return (nextCalled, context);
    }

    [Fact]
    public async Task AllowAnonymous_端點_不驗證直接放行()
    {
        var (nextCalled, context) = await Run(BuildContext(null, new AllowAnonymousAttribute()));

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        _session.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task 無任何憑證_回_401_不放行()
    {
        var (nextCalled, context) = await Run(BuildContext());

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task 有效_session_帶身分放行()
    {
        _session.Setup(s => s.GetAsync("abc", "123"))
            .ReturnsAsync(new Session { SessionId = "abc", DiscordId = 123, AccessToken = "a", RefreshToken = "r" });
        _player.Setup(p => p.GetAsync(123UL))
            .ReturnsAsync(new Player { DiscordId = 123, DiscordName = "n", Role = "user" });

        var (nextCalled, context) = await Run(BuildContext("discordId=123; sessionId123=abc"));

        Assert.True(nextCalled);
        Assert.Equal("123", context.User.FindFirst("discordId")?.Value);
    }

    [Fact]
    public async Task session_查無_回_403_不放行()
    {
        _session.Setup(s => s.GetAsync("abc", "123")).ReturnsAsync((Session?)null);

        var (nextCalled, context) = await Run(BuildContext("discordId=123; sessionId123=abc"));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task 有效_JWT_帶身分放行()
    {
        _jwt.Setup(j => j.ValidateToken("validtok"))
            .Returns(new JwtValidationResult { IsValid = true, DiscordId = 456, Role = "user" });

        var (nextCalled, context) = await Run(BuildContext("jwtToken=validtok"));

        Assert.True(nextCalled);
        Assert.Equal("456", context.User.FindFirst("discordId")?.Value);
    }

    [Fact]
    public async Task 角色不符_回_403_不放行()
    {
        _session.Setup(s => s.GetAsync("abc", "123"))
            .ReturnsAsync(new Session { SessionId = "abc", DiscordId = 123, AccessToken = "a", RefreshToken = "r" });
        _player.Setup(p => p.GetAsync(123UL))
            .ReturnsAsync(new Player { DiscordId = 123, DiscordName = "n", Role = "user" });

        // 端點要求 admin，但身分是 user
        var (nextCalled, context) = await Run(
            BuildContext("discordId=123; sessionId123=abc", new AuthorizeRoleAttribute("admin")));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }
}
