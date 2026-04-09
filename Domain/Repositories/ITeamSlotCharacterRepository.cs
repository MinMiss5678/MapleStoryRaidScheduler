using Domain.Entities;

namespace Domain.Repositories;

public interface ITeamSlotCharacterRepository
{
    Task CreateAsync(TeamSlotCharacter teamSlot);
    Task DeleteByTeamSlotIdAsync(int teamSlotId);
    Task DeleteCharacterAsync(TeamSlotCharacter teamSlotCharacter);
    Task DeleteByDiscordIdAndPeriodAsync(ulong discordId, DateTimeOffset startDateTime, DateTimeOffset endDateTime);
    Task UpdateAsync(TeamSlotCharacter teamSlotCharacter);
}