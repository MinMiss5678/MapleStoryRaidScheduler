using System.Security.Claims;
using Application.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Presentation.WebApi.Attributes;

namespace Presentation.WebApi.Middleware;

public class AuthenticationMiddleware : IMiddleware
{
    private readonly ISessionService _sessionService;
    private readonly IPlayerService _playerService;
    private readonly IJwtService _jwtService;
    private readonly IAuthService _authService;

    public AuthenticationMiddleware(ISessionService sessionService, IPlayerService playerService, IJwtService jwtService,
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

        var roleAttribute = endpoint?.Metadata.GetMetadata<AuthorizeRoleAttribute>();
        var identity = new ClaimsIdentity();

        context.Request.Cookies.TryGetValue("discordId", out var discordId);
            
        if (context.Request.Cookies.TryGetValue($"sessionId{discordId}", out var sessionId))
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var session = await _sessionService.GetAsync(sessionId, discordId);
            if (session == null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
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
            var validateTokenResult = _jwtService.ValidateToken(token);
            if (validateTokenResult.IsValid)
            {
                identity = new ClaimsIdentity(new[]
                {
                    new Claim("discordId", validateTokenResult.DiscordId.ToString())
                }, "jwt");
            }
            else if (validateTokenResult.Exception is SecurityTokenExpiredException)
            {
                var jwtTokenClaims = _jwtService.ReadJsonWebToken(token);
                var newJwt = await _authService.RefreshToken(jwtTokenClaims.DiscordId);
                if (newJwt != null)
                {
                    context.Response.Cookies.Append("jwtToken", newJwt, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteMode.Strict,
                        Expires = DateTimeOffset.UtcNow.AddDays(30)
                    });

                    identity = new ClaimsIdentity(new[]
                    {
                        new Claim("discordId", jwtTokenClaims.DiscordId.ToString())
                    }, "jwt");

                    context.User = new ClaimsPrincipal(identity);
                    await next(context);
                    return;
                }
            }
        }

        if (roleAttribute != null && identity.AuthenticationType != "session")
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!identity.Claims.Any() && roleAttribute == null)
        {
            // 如果是匿名訪問但有 token 驗證失敗的細節錯誤，可以在這裡處理，
            // 但既然是匿名訪問且驗證失敗，通常可以直接繼續或返回 401。
            // 這裡保持原有的 401 邏輯給那些非匿名且 token 無效的請求。
            context.Request.Cookies.TryGetValue("jwtToken", out var token);
            if (!string.IsNullOrEmpty(token))
            {
                var validateTokenResult = _jwtService.ValidateToken(token);
                if (!validateTokenResult.IsValid)
                {
                     context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                     await context.Response.WriteAsJsonAsync(new
                        { error = "TokenInvalid", detail = validateTokenResult.Exception.Message });
                     return;
                }
            }
        }

        if (identity.Claims.Count() != 0)
        {
            context.User = new ClaimsPrincipal(identity);
        }

        await next(context);
    }
}