using Domain.Entities;
using Domain.Repositories;
using Application.Interface;

namespace Infrastructure.Services;

public class BossService : IBossService
{
    private readonly IBossRepository _bossRepository;

    public BossService(IBossRepository bossRepository)
    {
        _bossRepository = bossRepository;
    }
    
    public async Task<IEnumerable<Boss>> GetAllAsync()
    {
        return await _bossRepository.GetAllAsync();
    }

    public async Task<IEnumerable<BossTemplate>> GetTemplatesByBossIdAsync(int bossId)
    {
        return await _bossRepository.GetTemplatesByBossIdAsync(bossId);
    }

    public async Task<BossTemplate?> GetTemplateByIdAsync(int templateId)
    {
        return await _bossRepository.GetTemplateByIdAsync(templateId);
    }

    public async Task<int> CreateTemplateAsync(BossTemplate template)
    {
        return await _bossRepository.CreateTemplateAsync(template);
    }

    public async Task<bool> UpdateTemplateAsync(BossTemplate template)
    {
        return await _bossRepository.UpdateTemplateAsync(template);
    }

    public async Task<bool> DeleteTemplateAsync(int templateId)
    {
        return await _bossRepository.DeleteTemplateAsync(templateId);
    }

    public async Task<int> CreateBossAsync(Boss boss)
    {
        return await _bossRepository.CreateBossAsync(boss);
    }

    public async Task<bool> UpdateBossAsync(Boss boss)
    {
        return await _bossRepository.UpdateBossAsync(boss);
    }

    public async Task<bool> DeleteBossAsync(int bossId)
    {
        return await _bossRepository.DeleteBossAsync(bossId);
    }
}