using System.ComponentModel.DataAnnotations;

namespace Domain.Entities;

public class Character
{
    [MaxLength(50)]
    [Required]
    public string Id { get; set; }
    public ulong DiscordId { get; set; }
    
    [MaxLength(20)]
    public string Name { get; set; }
    [MaxLength(5)]
    public string Job { get; set; }
    public int AttackPower { get; set; }
}