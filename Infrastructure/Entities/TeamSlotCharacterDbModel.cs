using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Domain.Attributes;

namespace Infrastructure.Entities;

[Table("TeamSlotCharacter")]
public class TeamSlotCharacterDbModel
{
    [Key]
    public int? Id { get; set; }
    public int TeamSlotId { get; set; }
    public long DiscordId { get; set; }
    public required string DiscordName { get; set; }
    public string? CharacterId { get; set; }
    public string? CharacterName { get; set; }
    public required string Job { get; set; }
    public int AttackPower { get; set; }
    public int Rounds { get; set; }
    public bool IsManual { get; set; }
}