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

    /// <summary>某隊已 Confirmed 的真實成員數（排除 vacancy 哨兵）——leader accept 容量把關。</summary>
    Task<int> CountConfirmedAsync(int teamSlotId);

    /// <summary>取單一成員列（含 xmin 版本），供 accept/decline。</summary>
    Task<TeamSlotCharacter?> GetByIdAsync(int id);

    /// <summary>xmin 樂觀鎖改狀態（Invited→Confirmed / →Rejected）。false = 版本對不上。</summary>
    Task<bool> UpdateStatusAsync(int id, string status, string version);
}
