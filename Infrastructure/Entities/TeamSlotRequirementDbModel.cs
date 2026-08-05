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
}
