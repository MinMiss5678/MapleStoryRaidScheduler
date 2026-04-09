using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

[Table("Boss")]
public class BossDbModel
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; }
    public int RequireMembers { get; set; }
    public int RoundConsumption { get; set; }
}