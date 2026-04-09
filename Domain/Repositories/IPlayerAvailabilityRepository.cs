using Domain.Entities;

namespace Domain.Repositories;

public interface IPlayerAvailabilityRepository
{
    Task CreateAsync(PlayerAvailability model);
    Task DeleteByPlayerRegisterIdAsync(int playerRegisterId);
    Task<IEnumerable<PlayerAvailability>> GetByPlayerRegisterIdAsync(int playerRegisterId);
    Task<IEnumerable<PlayerAvailability>> GetByDiscordIdAndPeriodIdAsync(ulong discordId, int periodId);
    Task<IEnumerable<PlayerAvailability>> GetByDiscordIdsAndPeriodIdAsync(List<ulong> discordId, int periodId);
}