namespace Domain.Entities;

/// <summary>
/// 隊伍來源（provenance）。取代舊 IsTemporary 布林。
/// 驅動：空隊自動清除（僅 Auto）、合併資格（僅 Auto）、重排保留（Admin 保留）。
/// </summary>
public static class TeamSlotSource
{
    public const string Auto = "auto";   // 玩家報名時系統自動建立
    public const string Admin = "admin"; // 管理員手動開團 / 批次重排
}
