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
/// 隊伍種類（period-less 重構）：排程（提前規劃）vs 即時（現揪現打）。同一張 TeamSlot 用此欄區分，非兩套系統。
/// 建立時明選、不靠「SlotDateTime 距現在多久」硬猜。見 plans/2026-08-11-realtime-team-formation.md §3.1。
/// </summary>
public static class TeamSlotKind
{
    public const string Scheduled = "Scheduled"; // 排程團：未來固定 SlotDateTime、常設可用時段配對
    public const string Instant = "Instant";     // 即時團：≈now、ExpiresAt(TTL)、LfgIntent 配對
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
    public const string Left = "Left";           // 玩家自願退隊的終態（別於 Rejected；行為同：不占容量、可重邀）
}
