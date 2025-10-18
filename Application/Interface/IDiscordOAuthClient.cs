using Application.DTOs;

namespace Application.Interface;

public interface IDiscordOAuthClient
{
    Task<DiscordTokenResponse> ExchangeCodeAsync(string code);
    Task<DiscordUserDto> GetUserAsync(string accessToken);
    Task<DiscordTokenResponse?> RefreshTokenAsync(string refreshToken);
    Task<IEnumerable<string>> GetUserRolesAsync(ulong discordId);
}