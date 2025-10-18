using Application.DTOs;
using Application.Interface;
using Application.Options;
using Domain.Entities;
using Microsoft.Extensions.Options;

namespace Application.Services;

public class AuthAppService
{
    private readonly IAuthService _authService;
    private readonly IDiscordOAuthClient _discordOAuthClient;
    private readonly IPlayerService _playerService;
    private readonly DiscordOptions _discordOptions;

    public AuthAppService(IAuthService authService, IDiscordOAuthClient discordOAuthClient,
        IPlayerService playerService, IOptions<DiscordOptions> discordOptions)
    {
        _authService = authService;
        _discordOAuthClient = discordOAuthClient;
        _playerService = playerService;
        _discordOptions = discordOptions.Value;
    }

    public async Task<LoginResult> LoginAsync(string code)
    {
        var (user, token) = await _authService.ExchangeCodeAsync(code);
        var roles = await _discordOAuthClient.GetUserRolesAsync(user.Id);

        if (roles.Contains(_discordOptions.AdminRoleId))
        {
            var sessionId = await _authService.CreateSessionAsync(user.Id, token);
            return new LoginResult
                { SessionId = sessionId, DiscordId = user.Id, Expiry = DateTimeOffset.UtcNow.AddDays(30) };
        }
        else if (roles.Contains(_discordOptions.UserRoleId))
        {
            var jwt = _authService.CreateJwt(user);
            await _playerService.CreateAsync(new Player()
            {
                DiscordId = user.Id,
                DiscordName = user.Name
            });
            return new LoginResult { JwtToken = jwt, Expiry = DateTimeOffset.UtcNow.AddDays(30) };
        }

        return new LoginResult();
    }

    public async Task<bool> LogoutAsync(string sessionId, string discordId)
    {
        return await _authService.DeleteSessionAsync(sessionId, discordId);
    }
}