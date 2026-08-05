using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

[Table("CharacterBossClear")]
public class CharacterBossClearDbModel
{
    [Key]
    public int Id { get; set; }
    public required string CharacterId { get; set; }
    public int BossId { get; set; }
    public int ClearCount { get; set; }
}
