namespace Application.DTOs;

/// <summary>玩家 profile（period-less §8：報名 UX 大改）——常設可用時段 + 角色參戰 opt-in。取代 per-period 報名。</summary>
public class ProfileDto
{
    public List<PlayerAvailabilityDto> Availabilities { get; set; } = [];
    public List<ProfileCharacterDto> Characters { get; set; } = [];
}

public class ProfileCharacterDto
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Job { get; set; }
    public int AttackPower { get; set; }
    public bool IsSeekingRaid { get; set; }
}
