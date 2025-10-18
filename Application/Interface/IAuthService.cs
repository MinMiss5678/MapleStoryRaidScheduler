using Domain.Entities;

namespace Application.Interface;

public interface IAuthService
{
    Task<(DiscordUser user, DiscordToken token)> ExchangeCodeAsync(string code);
    Task<string> CreateSessionAsync(ulong discordId, DiscordToken discordToken);
    string CreateJwt(DiscordUser discordUser);
    Task<bool> DeleteSessionAsync(string sessionId, string discordId);
    Task<string?> RefreshToken(ulong discordId);
}