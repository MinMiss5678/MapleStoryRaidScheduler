using Domain.Entities;

namespace Application.Interface;

public interface ISessionService
{
    Task<string> CreateAsync(ulong discordId);
    Task<Session?> GetAsync(string sessionId, string discordId);
    Task<bool> DeleteAsync(string sessionId, string discordId);
    Task DeleteByDiscordAsync(ulong discordId);
}
