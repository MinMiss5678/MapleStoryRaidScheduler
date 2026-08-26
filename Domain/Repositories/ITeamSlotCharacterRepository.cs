using Domain.Entities;

namespace Domain.Repositories;

public interface ITeamSlotCharacterRepository
{
    /// <summary>建立成員列，回傳新列的 Id（供邀請通知帶進按鈕 custom_id 走 accept/decline）。</summary>
    Task<int> CreateAsync(TeamSlotCharacter teamSlot);
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

    /// <summary>取某隊「某玩家的 Confirmed 成員」列（含 xmin），供玩家自助退隊。一人一隊至多一個 Confirmed。</summary>
    Task<TeamSlotCharacter?> GetConfirmedMemberAsync(int teamSlotId, ulong discordId);

    /// <summary>玩家退隊：Confirmed→Left、寫 LeftAt=now()，xmin 樂觀鎖。false = 版本對不上。</summary>
    Task<bool> LeaveAsync(int id, string version);

    /// <summary>某隊 active（Confirmed/Invited/Applied）成員的 DiscordId 集合——供候選去重（排除已在隊/待處理者）。</summary>
    Task<IReadOnlyCollection<ulong>> GetActiveMemberDiscordIdsAsync(int teamSlotId);

    /// <summary>
    /// 已在某開團時刻（精確 SlotDateTime）別隊 Confirmed 的 DiscordId 集合——供候選排除「不可分身」者
    /// （對齊 uq_tsc_confirmed_overlap；period-less §8 Phase 2）。
    /// </summary>
    Task<IReadOnlyCollection<ulong>> GetConfirmedDiscordIdsAtAsync(DateTimeOffset slotDateTime);

    /// <summary>
    /// 隊伍額滿時，把該隊其餘「待接受邀請（Invited）」一次撤銷為 Rejected；回傳被撤銷者的被邀玩家 DiscordId + 該邀請 DM 的 message id
    /// （dm-revoke-cleanup：用 message id 編輯被邀者 DM 成「已失效」）。只動 Invited；不動 Applied（保留候補）。
    /// </summary>
    Task<IReadOnlyCollection<RevokedInvite>> RevokePendingInvitesAsync(int teamSlotId);

    /// <summary>
    /// 撤銷某隊「指定職業」的待接受邀請（composition-quota：定案後某職業名額已滿 → 只撤該職業的 pending，保留其他職業）。
    /// 回傳被撤者 (DiscordId, DmMessageId) 供編輯 DM。只動 Invited、不動 Applied。
    /// </summary>
    Task<IReadOnlyCollection<RevokedInvite>> RevokePendingInvitesByJobsAsync(int teamSlotId, IReadOnlyCollection<string> jobs);

    /// <summary>某時間範圍內所有 Confirmed 訂位的 (SlotDateTime, DiscordId)——招募熱力圖一次撈、in-memory 分格扣「不可分身」。</summary>
    Task<IReadOnlyCollection<ConfirmedBooking>> GetConfirmedBookingsInRangeAsync(DateTimeOffset from, DateTimeOffset to);
}

/// <summary>被自動撤銷的一筆邀請：被邀玩家 DiscordId + 該邀請 DM 的 message id（null＝DM 尚未送出/id 未回寫 → 跳過清理）。</summary>
public record RevokedInvite(ulong DiscordId, ulong? DmMessageId);

/// <summary>一筆已確認訂位：精確開團時刻 + 玩家 DiscordId（招募熱力圖扣不可分身用）。</summary>
public record ConfirmedBooking(DateTimeOffset SlotDateTime, ulong DiscordId);
