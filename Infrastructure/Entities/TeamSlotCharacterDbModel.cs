using System.ComponentModel.DataAnnotations.Schema;
using Domain.Attributes;

namespace Infrastructure.Entities;

[Table("TeamSlotCharacter")]
public class TeamSlotCharacterDbModel
{
    [ExplicitKey]
    public int TeamSlotId { get; set; }
    public string CharacterId { get; set; }
}