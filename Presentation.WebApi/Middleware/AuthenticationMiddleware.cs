using System.Security.Claims;
using Application.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Presentation.WebApi.Middleware;

/// <summary>
/// 認證（只回 401）：讀 cookie → 驗 session／JWT → 建立 context.User。
/// 不做角色授權——授權在下游的 <see cref="RoleAuthorizationMiddleware"/>，所以任何成功路徑的 <c>await next()</c> 都會經過授權。
/// </summary>
public class AuthenticationMiddleware : IMiddleware
{
    private readonly ISessionService _sessionService;
    private readonly IPlayerService _playerService;
    private readonly IJwtService _jwtService;
    private readonly IAuthService _authService;

    public AuthenticationMiddleware(ISessionService sessionService, IPlayerService playerService,
        IJwtService jwtService,
        IAuthService authService)
    {
        _sessionService = sessionService;
        _playerService = playerService;
        _jwtService = jwtService;
        _authService = authService;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var endpoint = context.GetEndpoint();

        var allowAnonymous = endpoint?.Metadata.GetMetadata<IAllowAnonymous>();
        if (allowAnonymous != null)
        {
            await next(context);
            return;
        }

        var identity = new ClaimsIdentity();

        context.Request.Cookies.TryGetValue("discordId", out var discordId);

        if (context.Request.Cookies.TryGetValue($"sessionId{discordId}", out var sessionId))
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var session = await _sessionService.GetAsync(sessionId, discordId!);
            if (session == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Cookies.Delete($"sessionId{discordId}");
                return;
            }

            context.Response.Cookies.Append($"sessionId{discordId}", sessionId, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });

            var player = await _playerService.GetAsync(session.DiscordId);

            identity = new ClaimsIdentity(new[]
            {
                new Claim("discordId", session.DiscordId.ToString()),
                new Claim(ClaimTypes.Role, player?.Role ?? "")
            }, "session");
        }

        if (!identity.Claims.Any())
        {
            context.Request.Cookies.TryGetValue("jwtToken", out var token);
            var validateTokenResult = _jwtService.ValidateToken(token!);
            if (!validateTokenResult.IsValid && validateTokenResult.Exception is SecurityTokenExpiredException)
            {
                var claims = _jwtService.ReadJsonWebToken(token!);
                var newJwt = await _authService.RefreshToken(claims.DiscordId);
                if (!string.IsNullOrEmpty(newJwt))
                {
                    var refreshed = _jwtService.ValidateToken(newJwt);
                    if (!refreshed.IsValid || refreshed.DiscordId != claims.DiscordId)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }

                    context.Response.Cookies.Append("jwtToken", newJwt, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteMode.Strict,
                        Expires = DateTimeOffset.UtcNow.AddDays(30)
                    });
                    validateTokenResult = refreshed;
                }
            }

            // 刷新後使用新 token 的身分與角色；角色授權由下游 RoleAuthorizationMiddleware 執行。
            if (validateTokenResult.IsValid)
            {
                // Role 從 JWT claim 讀取，不查 DB（真無狀態）
                identity = new ClaimsIdentity(new[]
                {
                    new Claim("discordId", validateTokenResult.DiscordId.ToString()),
                    new Claim(ClaimTypes.Role, validateTokenResult.Role ?? "")
                }, "jwt");
            }
        }

        if (!identity.Claims.Any())
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.User = new ClaimsPrincipal(identity);

        await next(context);
    }
}
