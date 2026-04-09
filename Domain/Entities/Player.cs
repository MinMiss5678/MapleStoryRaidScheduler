using System.ComponentModel.DataAnnotations;

namespace Domain.Entities;

public class Player
{
    public ulong DiscordId { get; set; }
    [MaxLength(50)]
    public string DiscordName { get; set; }
    public string Role { get; set; }
}