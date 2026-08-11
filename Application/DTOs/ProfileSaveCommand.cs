namespace Application.DTOs;

/// <summary>儲存 profile：常設可用時段（replace-all）+ 參戰角色 opt-in（DiscordId 由 Controller 注入）。</summary>
public class ProfileSaveCommand
{
    public ulong DiscordId { get; set; }
    public List<PlayerAvailabilityDto> Availabilities { get; set; } = [];
    public List<string> SeekingCharacterIds { get; set; } = [];
}
