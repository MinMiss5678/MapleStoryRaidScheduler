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
    public string? Description { get; set; }     // 隊伍說明/公告
}
