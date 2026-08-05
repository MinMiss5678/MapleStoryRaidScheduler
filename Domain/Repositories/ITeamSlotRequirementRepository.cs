using Domain.Entities;

namespace Domain.Repositories;

public interface ITeamSlotRequirementRepository
{
    /// <summary>建需求列 + 其可接受職業，回 requirement id。</summary>
    Task<int> CreateAsync(TeamSlotRequirement requirement);

    /// <summary>取某隊的所有需求列（含各列的 Jobs）。</summary>
    Task<IEnumerable<TeamSlotRequirement>> GetByTeamSlotIdAsync(int teamSlotId);

    /// <summary>刪某隊的所有需求列（子表 TeamSlotRequirementJob 由 FK ON DELETE CASCADE 連帶清）。</summary>
    Task DeleteByTeamSlotIdAsync(int teamSlotId);
}
