namespace Domain.Repositories;

public interface ISessionRepository
{
    Task<int> CreateAsync(string sessionId, ulong discordId);
    Task<int> ExtendAsync(string sessionId, DateTimeOffset sessionExpiry);
    Task<bool> DeleteAsync(string id);
    Task DeleteByDiscordAsync(ulong discordId);
}
