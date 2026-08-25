using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

[Table("TeamSlotRequirement")]
public class TeamSlotRequirementDbModel
{
    [Key]
    public int Id { get; set; }
    public int TeamSlotId { get; set; }
    public int Count { get; set; }
    public int MinClearCount { get; set; }
    public int MinLevel { get; set; }          // 人物等級門檻（group 層硬篩，migration 000023）
}
