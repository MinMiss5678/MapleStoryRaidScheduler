using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

[Table("BossTemplate")]
public class BossTemplateDbModel
{
    [Key]
    public int Id { get; set; }
    public int BossId { get; set; }
    public required string Name { get; set; }
}
