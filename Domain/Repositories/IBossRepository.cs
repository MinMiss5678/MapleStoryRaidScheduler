using Domain.Entities;

namespace Domain.Repositories;

public interface IBossRepository
{
    Task<IEnumerable<Boss>> GetAllAsync();
    Task<Boss?> GetByIdAsync(int bossId);
    Task<int> CreateBossAsync(Boss boss);
    Task<bool> UpdateBossAsync(Boss boss);
    Task<bool> DeleteBossAsync(int bossId);
}
