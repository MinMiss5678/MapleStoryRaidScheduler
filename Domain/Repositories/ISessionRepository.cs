using Domain.Entities;

namespace Domain.Repositories;

public interface ISessionRepository
{
    Task<int> CreateAsync(string sessionId, ulong discordId, DiscordToken token);
    Task<int> UpdateAsync(Session session);
    Task<bool> DeleteAsync(string id);
    Task DeleteByDiscordAsync(ulong discordId);
}