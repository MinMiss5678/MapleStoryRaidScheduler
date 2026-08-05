using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

[Table("TeamSlotRequirementJob")]
public class TeamSlotRequirementJobDbModel
{
    [Key]
    public int Id { get; set; }
    public int RequirementId { get; set; }
    public required string Job { get; set; }
    public int MinAttackPower { get; set; }
}
