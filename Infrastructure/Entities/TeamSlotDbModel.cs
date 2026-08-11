using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

[Table("TeamSlot")]
public class TeamSlotDbModel
{
    [Key]
    public int Id { get; set; }
    public int BossId { get; set; }
    public DateTimeOffset SlotDateTime { get; set; }
    public string Source { get; set; } = "auto";
    public int? TemplateId { get; set; }
    public int? PeriodId { get; set; }          // leader-led（migration 000009）：週期權威歸屬
    public long? LeaderDiscordId { get; set; }  // 隊長歸屬；null=未認領草稿
    public long? PendingLeaderDiscordId { get; set; }  // 隊長轉讓待接受目標
    public string? Description { get; set; }     // 隊伍說明/公告
    public string Kind { get; set; } = "Scheduled";   // period-less：Scheduled=排程 / Instant=即時
    public DateTimeOffset? ExpiresAt { get; set; }     // 即時團 TTL 到期
    public int? RunsMin { get; set; }                  // 場數範圍（選填、僅告示）
    public int? RunsMax { get; set; }
}
