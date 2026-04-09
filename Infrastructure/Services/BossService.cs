using Application.Exceptions;
using Application.Interface;
using Domain.Entities;
using Domain.Repositories;

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

    public async Task<BossTemplate> GetTemplateByIdAsync(int templateId)
    {
        var template = await _bossRepository.GetTemplateByIdAsync(templateId);
        if (template == null) throw new NotFoundException($"BossTemplate {templateId} not found");
        return template;
    }

    public async Task<int> CreateTemplateAsync(BossTemplate template)
    {
        return await _bossRepository.CreateTemplateAsync(template);
    }

    public async Task UpdateTemplateAsync(BossTemplate template)
    {
        var ok = await _bossRepository.UpdateTemplateAsync(template);
        if (!ok) throw new NotFoundException($"BossTemplate {template.Id} not found");
    }

    public async Task DeleteTemplateAsync(int templateId)
    {
        var ok = await _bossRepository.DeleteTemplateAsync(templateId);
        if (!ok) throw new NotFoundException($"BossTemplate {templateId} not found");
    }

    public async Task<int> CreateBossAsync(Boss boss)
    {
        return await _bossRepository.CreateBossAsync(boss);
    }

    public async Task UpdateBossAsync(Boss boss)
    {
        var ok = await _bossRepository.UpdateBossAsync(boss);
        if (!ok) throw new NotFoundException($"Boss {boss.Id} not found");
    }

    public async Task DeleteBossAsync(int bossId)
    {
        var templates = await _bossRepository.GetTemplatesByBossIdAsync(bossId);
        foreach (var template in templates)
        {
            await _bossRepository.DeleteTemplateAsync(template.Id);
        }

        var ok = await _bossRepository.DeleteBossAsync(bossId);
        if (!ok) throw new NotFoundException($"Boss {bossId} not found");
    }
}