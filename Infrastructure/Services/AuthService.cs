using Application.Interface;
using Application.Options;
using Domain.Entities;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly IDiscordOAuthClient _discordClient;
    private readonly ISessionService _sessionService;
    private readonly IJwtService _jwtService;
    private readonly DiscordOptions _discordOptions;

    public AuthService(IDiscordOAuthClient discordClient, ISessionService sessionService, IJwtService jwtService, IOptions<DiscordOptions> discordOptions)
    {
        _discordClient = discordClient;
        _sessionService = sessionService;
        _jwtService = jwtService;
        _discordOptions = discordOptions.Value;
    }

    public async Task<(DiscordUser user, DiscordToken token)> ExchangeCodeAsync(string code)
    {
        var tokenResponse = await _discordClient.ExchangeCodeAsync(code);
        var token = new DiscordToken()
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            ExpiresIn = tokenResponse.ExpiresIn,
        };

        var userDto = await _discordClient.GetUserAsync(token.AccessToken);
        var user = new DiscordUser()
        {
            Id = userDto.Id,
            Name = userDto.Username,
        };

        return (user, token);
    }

    public async Task<string> CreateSessionAsync(ulong discordId, DiscordToken discordToken)
        => await _sessionService.CreateAsync(discordId, discordToken);

    public string CreateJwt(DiscordUser discordUser)
        => _jwtService.CreateToken(discordUser);

    public async Task<bool> DeleteSessionAsync(string sessionId, string discordId)
        => await _sessionService.DeleteAsync(sessionId, discordId);

    public async Task<string?> RefreshToken(ulong discordId)
    {
        var roles = await _discordClient.GetUserRolesAsync(discordId);
        if (roles.Contains(_discordOptions.UserRoleId))
        {
            return CreateJwt(new DiscordUser()
            {
                Id = discordId,
            });
        }

        return null;
    }
}