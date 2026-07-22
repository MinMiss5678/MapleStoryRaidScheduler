using System.ComponentModel.DataAnnotations;

namespace Domain.Entities;

public class Character
{
    [MaxLength(50)]
    [Required]
    public required string Id { get; set; }
    public ulong DiscordId { get; set; }

    [MaxLength(20)]
    public required string Name { get; set; }
    [MaxLength(5)]
    public required string Job { get; set; }
    public int AttackPower { get; set; }
}
