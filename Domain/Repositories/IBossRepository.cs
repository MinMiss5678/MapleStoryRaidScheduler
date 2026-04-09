using Domain.Entities;

namespace Domain.Repositories;

public interface IBossRepository
{
    Task<IEnumerable<Boss>> GetAllAsync();
    Task<IEnumerable<BossTemplate>> GetTemplatesByBossIdAsync(int bossId);
    Task<BossTemplate?> GetTemplateByIdAsync(int templateId);
    Task<int> CreateTemplateAsync(BossTemplate template);
    Task<bool> UpdateTemplateAsync(BossTemplate template);
    Task<bool> DeleteTemplateAsync(int templateId);
    Task<int> CreateBossAsync(Boss boss);
    Task<bool> UpdateBossAsync(Boss boss);
    Task<bool> DeleteBossAsync(int bossId);
}