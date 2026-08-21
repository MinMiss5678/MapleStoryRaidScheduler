namespace Domain.Entities;

public class TeamSlot
{
    public int Id { get; set; }
    public int BossId { get; set; }
    public string? BossName { get; set; }
    public DateTimeOffset SlotDateTime { get; set; }
    public string Source { get; set; } = TeamSlotSource.Leader;      // period-less：leader 開隊；auto/admin 已退場

    /// <summary>隊長歸屬（leader-led，§3）。null=未認領草稿。</summary>
    public ulong? LeaderDiscordId { get; set; }

    /// <summary>隊長轉讓（需同意）：提議轉給的目標；等對方接受才搬進 LeaderDiscordId。null=無待處理轉讓。</summary>
    public ulong? PendingLeaderDiscordId { get; set; }

    /// <summary>隊伍說明/公告（leader-led，§3 吸收非結構化招募需求）。</summary>
    public string? Description { get; set; }

    /// <summary>隊伍種類（period-less 重構，§3.1）：Scheduled=排程 / Instant=即時。預設 Scheduled。</summary>
    public string Kind { get; set; } = TeamSlotKind.Scheduled;

    /// <summary>即時團 TTL 到期時刻（Instant 專用）；Scheduled 為 null，用 SlotDateTime 自然到期。</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>場數範圍（選填、僅告示不強制，§3.1）：隊長公告這團打幾場。1~3 都可＝(1,3)、固定 2＝(2,2)、null=隨意。</summary>
    public int? RunsMin { get; set; }
    public int? RunsMax { get; set; }
}
