using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

[Table("BossTemplateRequirement")]
public class BossTemplateRequirementDbModel
{
    [Key]
    public int Id { get; set; }
    public int BossTemplateId { get; set; }
    public required string JobCategory { get; set; }
    public int Count { get; set; }
    public int Priority { get; set; }
}
