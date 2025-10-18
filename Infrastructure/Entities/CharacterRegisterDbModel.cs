using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

[Table("CharacterRegister")]
public class CharacterRegisterDbModel
{
    [Key]
    public int Id { get; set; }
    public int PlayerRegisterId { get; set; }
    public string CharacterId { get; set; }
    public string Job { get; set; }
    public int BossId { get; set; }
    public int Rounds { get; set; }
}