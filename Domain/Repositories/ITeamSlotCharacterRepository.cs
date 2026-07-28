using Domain.Entities;

namespace Domain.Repositories;

public interface ITeamSlotCharacterRepository
{
    Task CreateAsync(TeamSlotCharacter teamSlot);
    Task DeleteByTeamSlotIdAsync(int teamSlotId);
    Task DeleteCharacterAsync(TeamSlotCharacter teamSlotCharacter);
    Task DeleteByDiscordIdAndPeriodAsync(ulong discordId, DateTimeOffset startDateTime, DateTimeOffset endDateTime);
    /// <summary>樂觀鎖版本比對更新（xmin）。回傳 false 代表版本對不上，這期間已被別的流程動過。</summary>
    Task<bool> UpdateAsync(TeamSlotCharacter teamSlotCharacter);
}
