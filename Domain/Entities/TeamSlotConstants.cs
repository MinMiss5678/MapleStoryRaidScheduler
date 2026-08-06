namespace Domain.Entities;

/// <summary>
/// 隊伍來源（provenance）。取代舊 IsTemporary 布林。
/// 驅動：空隊自動清除（僅 Auto）、合併資格（僅 Auto）、重排保留（Admin 保留）。
/// </summary>
public static class TeamSlotSource
{
    public const string Auto = "auto";     // 玩家報名時系統自動建立（leader-led contract 後退場）
    public const string Admin = "admin";   // 管理員手動開團 / 批次重排
    public const string Leader = "leader"; // 隊長主導開團（leader-led）
}

/// <summary>
/// 成員入隊狀態（leader-led）。只有 <see cref="Confirmed"/> 占容量；<see cref="Applied"/>/<see cref="Invited"/> 皆不占。
/// 見計畫 §9.4。舊模型的既有成員一律視為 Confirmed（migration 000009 DEFAULT）。
/// </summary>
public static class TeamSlotMemberStatus
{
    public const string Applied = "Applied";     // Push：玩家申請中
    public const string Invited = "Invited";     // Pull：隊長邀請中
    public const string Confirmed = "Confirmed"; // 雙方同意、占容量
    public const string Rejected = "Rejected";   // 任一方拒絕/取消的終態
}
