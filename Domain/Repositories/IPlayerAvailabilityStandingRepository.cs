using Domain.Entities;

namespace Domain.Repositories;

/// <summary>
/// 常設可用時段（period-less §8 Phase 2）：掛玩家（DiscordId）、非每週報名。
/// 取代掛 PlayerRegisterId 的舊 <see cref="IPlayerAvailabilityRepository"/>（Phase 4 一併退場）。
/// </summary>
public interface IPlayerAvailabilityStandingRepository
{
    Task CreateAsync(PlayerAvailability model);

    /// <summary>清掉某玩家全部常設時段（報名/編輯採 replace-all 語意）。</summary>
    Task DeleteByDiscordIdAsync(ulong discordId);
}
