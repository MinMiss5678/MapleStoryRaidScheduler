using System.ComponentModel.DataAnnotations.Schema;
using Domain.Attributes;

namespace Infrastructure.Entities;

[Table("Player")]
public class PlayerDbModel
{
    [ExplicitKey]
    public long DiscordId { get; set; }
    public required string DiscordName { get; set; }
    public required string Role { get; set; }
}