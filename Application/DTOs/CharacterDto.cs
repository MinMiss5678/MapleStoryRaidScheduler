using System.Collections.Generic;

namespace Application.DTOs;

public class CharacterDto
{
    public required string Id { get; set; }
    public ulong DiscordId { get; set; }
    public required string DiscordName { get; set; }

    public required string Name { get; set; }
    public required string Job { get; set; }
    public int AttackPower { get; set; }
    public int Rounds { get; set; }
    public int[] RegisteredPeriodIds { get; set; } = Array.Empty<int>();
}