using Domain.Entities;

namespace Domain.Repositories;

public interface ITeamSlotCharacterRepository
{
    Task CreateAsync(TeamSlotCharacter teamSlot);
    Task DeleteByTeamSlotIdAsync(int teamSlotId);
    Task DeleteCharacterAsync(TeamSlotCharacter teamSlotCharacter);
}