using System.ComponentModel.DataAnnotations.Schema;
using Domain.Attributes;

namespace Infrastructure.Entities;

[Table("Character")]
public class CharacterDbModel
{
    [ExplicitKey]
    public required string Id { get; set; }

    public long DiscordId { get; set; }

    public required string Name { get; set; }
    public required string Job { get; set; }
    public int AttackPower { get; set; }
}