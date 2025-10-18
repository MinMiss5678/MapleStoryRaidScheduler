using Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Presentation.WebApi.Controller;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthAppService _authAppService;

    public AuthController(AuthAppService authAppService)
    {
        _authAppService = authAppService;
    }

    public class CallbackRequest
    {
        public string Code { get; set; } = default!;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] CallbackRequest request)
    {
        var result = await _authAppService.LoginAsync(request.Code);
        if (result.IsSession)
        {
            Response.Cookies.Append($"sessionId{result.DiscordId}", result.SessionId, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = result.Expiry
            });
            
            Response.Cookies.Append("discordId", result.DiscordId.ToString(), new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = result.Expiry
            });

            return Ok(new { success = true, type = "session" });
        }
        else if (result.IsJwt)
        {
            Response.Cookies.Append("jwtToken", result.JwtToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = result.Expiry
            });
            
            return Ok(new { success = true, type = "jwt" });
        }
        else
        {
            return Unauthorized();
        }
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync()
    {
        var discordId = Request.Cookies["discordId"];
        var sessionId = Request.Cookies[$"sessionId{discordId}"];

        if (sessionId != null)
        {
            var result = await _authAppService.LogoutAsync(sessionId, discordId);
            if (!result)
                return StatusCode(500, new { success = false, message = "Failed to delete session" });    
        }

        Response.Cookies.Delete($"sessionId{discordId}");
        Response.Cookies.Delete("jwtToken");
        Response.Cookies.Delete("discordId");
        
        return Ok(new { success = true });
    }
}