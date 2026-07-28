using Domain.Entities;

namespace Application.Interface;

public interface IAuthService
{
    Task<DiscordUser> ExchangeCodeAsync(string code);
    Task<string> CreateSessionAsync(ulong discordId);
    string CreateJwt(DiscordUser discordUser, string role);
    Task<bool> DeleteSessionAsync(string sessionId, string discordId);
    Task<string?> RefreshToken(ulong discordId);
}
