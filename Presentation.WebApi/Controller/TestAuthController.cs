using Application.Interface;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Presentation.WebApi.Controller;

/// <summary>
/// E2E 測試專用登入端點——跳過 Discord OAuth，依捏造身分直接發「與正式流程一樣的」cookie。
/// 🔴 只在非 Production 有效（Production 一律 404）；另可設 E2E:TestLoginSecret 要求 X-Test-Secret 再上一道鎖。
/// 這是認證後門，務必確保 Production build 打不到（環境旗標 + 可選 secret）。
/// </summary>
[ApiController]
[Route("api/test")]
[AllowAnonymous]
public class TestAuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IPlayerService _playerService;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public TestAuthController(IAuthService authService, IPlayerService playerService,
        IWebHostEnvironment env, IConfiguration config)
    {
        _authService = authService;
        _playerService = playerService;
        _env = env;
        _config = config;
    }

    public class TestLoginRequest
    {
        public ulong DiscordId { get; set; }
        public string DiscordName { get; set; } = "E2E";
        public string Role { get; set; } = "user"; // "user" | "admin"
    }

    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] TestLoginRequest request)
    {
        // 🔴 鎖死 Production：這條路在正式環境等於不存在
        if (_env.IsProduction())
            return NotFound();

        // 可選第二道鎖：設了 secret 就要求 header 相符
        var secret = _config["E2E:TestLoginSecret"];
        if (!string.IsNullOrEmpty(secret) && Request.Headers["X-Test-Secret"] != secret)
            return Unauthorized();

        // 讓身分在 DB 真實存在，下游流程（報名 / 補位…）才接得上
        await _playerService.CreateAsync(new Player
        {
            DiscordId = request.DiscordId,
            DiscordName = request.DiscordName,
            Role = request.Role
        });

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true, // localhost 視為 secure context，HTTP 下瀏覽器仍接受
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        };

        if (request.Role == "admin")
        {
            // session 用假 DiscordToken（E2E 不會觸發 refresh）
            var sessionId = await _authService.CreateSessionAsync(request.DiscordId,
                new DiscordToken { AccessToken = "e2e", RefreshToken = "e2e", ExpiresIn = 3600 });
            Response.Cookies.Append($"sessionId{request.DiscordId}", sessionId, cookieOptions);
            Response.Cookies.Append("discordId", request.DiscordId.ToString(), cookieOptions);
        }
        else
        {
            var jwt = _authService.CreateJwt(
                new DiscordUser { Id = request.DiscordId, Name = request.DiscordName }, request.Role);
            Response.Cookies.Append("jwtToken", jwt, cookieOptions);
        }

        return Ok(new { role = request.Role });
    }
}
