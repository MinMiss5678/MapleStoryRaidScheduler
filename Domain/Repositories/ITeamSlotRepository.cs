using Domain.Entities;

namespace Domain.Repositories;

public interface ITeamSlotRepository
{
    Task<int> CreateAsync(TeamSlot teamSlot);
    Task DeleteAsync(int id);
    Task<TeamSlot?> GetByIdAsync(int id);
    Task<IEnumerable<TeamSlot>> GetByPeriodIdAsync(int periodId);
    Task<IEnumerable<TeamSlot>> GetIncompleteTeamsAsync(int bossId, int periodId);
    Task<IEnumerable<TeamSlot>> GetTemporaryByPeriodIdAsync(int periodId);
    Task UpdateAsync(TeamSlot teamSlot);
}