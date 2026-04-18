using Application.Interface;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly IDiscordOAuthClient _discordClient;
    private readonly ISessionService _sessionService;
    private readonly IJwtService _jwtService;
    private readonly IPlayerRepository _playerRepository;

    public AuthService(IDiscordOAuthClient discordClient, ISessionService sessionService, IJwtService jwtService, IPlayerRepository playerRepository)
    {
        _discordClient = discordClient;
        _sessionService = sessionService;
        _jwtService = jwtService;
        _playerRepository = playerRepository;
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
        // Player.Role 已透過 MemberUpdatedHandler 即時同步，直接讀 DB 即可
        var player = await _playerRepository.GetAsync(discordId);
        if (string.IsNullOrEmpty(player?.Role))
            return null;

        return CreateJwt(new DiscordUser
        {
            Id = discordId,
            Name = player.DiscordName ?? string.Empty,
        });
    }
}