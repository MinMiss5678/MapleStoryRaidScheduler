using Application.DTOs;

namespace Application.Interface;

/// <summary>玩家 profile（period-less）：常設可用時段 + 角色參戰 opt-in。取代 per-period 報名。</summary>
public interface IProfileService
{
    Task<ProfileDto> GetAsync(ulong discordId);
    Task SaveAsync(ProfileSaveCommand command);
}
