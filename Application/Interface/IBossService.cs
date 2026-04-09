using Domain.Entities;

namespace Application.Interface;

public interface IBossService
{
    Task<IEnumerable<Boss>> GetAllAsync();
    Task<IEnumerable<BossTemplate>> GetTemplatesByBossIdAsync(int bossId);
    Task<BossTemplate> GetTemplateByIdAsync(int templateId);
    Task<int> CreateTemplateAsync(BossTemplate template);
    Task UpdateTemplateAsync(BossTemplate template);
    Task DeleteTemplateAsync(int templateId);
    Task<int> CreateBossAsync(Boss boss);
    Task UpdateBossAsync(Boss boss);
    Task DeleteBossAsync(int bossId);
}