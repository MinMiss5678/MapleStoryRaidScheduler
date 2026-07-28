namespace Domain.Repositories;

public interface ISessionRepository
{
    Task<int> CreateAsync(string sessionId, ulong discordId);
    Task<bool> DeleteAsync(string id);
    Task DeleteByDiscordAsync(ulong discordId);
}
