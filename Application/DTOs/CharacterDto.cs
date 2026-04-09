using System.Collections.Generic;

namespace Application.DTOs;

public class CharacterDto
{
    public string Id { get; set; }
    public ulong DiscordId { get; set; }
    public string DiscordName { get; set; }

    public string Name { get; set; }
    public string Job { get; set; }
    public int AttackPower { get; set; }
    public int Rounds { get; set; }
    public int[] RegisteredPeriodIds { get; set; } = Array.Empty<int>();
}