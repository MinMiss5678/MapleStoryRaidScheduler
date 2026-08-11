using Domain.Entities;

namespace Domain.Repositories;

/// <summary>可用時段日期 override 的寫入/查詢（period-less §8 Phase 2b-write）。玩家自助管理自己的例外。</summary>
public interface IPlayerAvailabilityOverrideRepository
{
    Task CreateAsync(PlayerAvailabilityOverride model);
    /// <summary>刪自己的 override（IDOR 防護：WHERE DiscordId AND Id）。回傳影響列數。</summary>
    Task<int> DeleteAsync(ulong discordId, int id);
    Task<IEnumerable<PlayerAvailabilityOverride>> GetByDiscordIdAsync(ulong discordId);
}
