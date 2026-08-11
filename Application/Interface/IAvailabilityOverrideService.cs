using Application.DTOs;

namespace Application.Interface;

/// <summary>玩家自助管理可用時段的日期 override（period-less §8 Phase 2b-write）。</summary>
public interface IAvailabilityOverrideService
{
    Task<IEnumerable<AvailabilityOverrideDto>> GetMineAsync(ulong discordId);
    Task AddAsync(AvailabilityOverrideCreateCommand command);
    Task RemoveAsync(ulong discordId, int id);
}
