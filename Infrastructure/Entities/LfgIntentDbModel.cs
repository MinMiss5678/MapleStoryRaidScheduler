using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

[Table("LfgIntent")]
public class LfgIntentDbModel
{
    [Key]
    public int Id { get; set; }
    public long DiscordId { get; set; }
    public required string CharacterId { get; set; }
    public int BossId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
