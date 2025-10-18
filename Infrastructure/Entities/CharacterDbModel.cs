using System.ComponentModel.DataAnnotations.Schema;
using Domain.Attributes;

namespace Infrastructure.Entities;

[Table("Character")]
public class CharacterDbModel
{
    [ExplicitKey]
    public string Id { get; set; }
    
    public long DiscordId { get; set; }

    public string Name { get; set; }
    public string Job { get; set; }
    public int AttackPower { get; set; }
}