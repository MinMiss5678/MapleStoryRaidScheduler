using System.ComponentModel.DataAnnotations;

namespace Domain.Entities;

public class Player
{
    public ulong DiscordId { get; set; }
    [MaxLength(50)]
    public required string DiscordName { get; set; }
    public required string Role { get; set; }
}
