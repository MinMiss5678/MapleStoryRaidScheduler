using Application.DTOs;
using Domain.Entities;

namespace Application.Interface;

public interface IBossService
{
    Task<IEnumerable<Boss>> GetAllAsync();
    Task<IEnumerable<BossTemplate>> GetTemplatesByBossIdAsync(int bossId);
    Task<BossTemplate> GetTemplateByIdAsync(int templateId);
    Task<int> CreateTemplateAsync(BossTemplateRequest request);
    Task UpdateTemplateAsync(int id, BossTemplateRequest request);
    Task DeleteTemplateAsync(int templateId);
    Task<int> CreateBossAsync(BossRequest request);
    Task UpdateBossAsync(int id, BossRequest request);
    Task DeleteBossAsync(int bossId);
}