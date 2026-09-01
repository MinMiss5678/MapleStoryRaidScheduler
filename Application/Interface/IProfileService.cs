using Application.DTOs;

namespace Application.Interface;

/// <summary>玩家 profile（period-less）：常設可用時段 + 角色參戰 opt-in。取代 per-period 報名。</summary>
public interface IProfileService
{
    Task<ProfileDto> GetAsync(ulong discordId);
    Task SaveAsync(ProfileSaveCommand command);

    // ── 新鮮度提醒（階段二）DM 按鈕動作，見 plans/2026-09-01-availability-freshness-decay.md ──
    /// <summary>留任：更新玩家最後活躍時戳（＝重置新鮮度衰退時鐘）。</summary>
    Task ReaffirmFreshnessAsync(ulong discordId);
    /// <summary>移除我：關閉該玩家所有角色的參戰（退出候選池）；**保留常設時段資料**，隨時可重開。</summary>
    Task OptOutSeekingAsync(ulong discordId);
}
