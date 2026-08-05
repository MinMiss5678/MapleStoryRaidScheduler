using Application.DTOs;
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

    public async Task<int> CreateTemplateAsync(BossTemplateRequest request)
    {
        // 先驗 Boss 存在，否則 BossId 不存在時 INSERT 會 FK 違反 → 500（回乾淨 404）
        if (await _bossRepository.GetByIdAsync(request.BossId) == null)
            throw new NotFoundException($"Boss {request.BossId} not found");

        var template = new BossTemplate
        {
            BossId = request.BossId,
            Name = request.Name,
            Requirements = request.Requirements.Select(r => new BossTemplateRequirement
            {
                JobCategory = r.JobCategory,
                Count = r.Count,
                Priority = r.Priority,
                MinLevel = r.MinLevel,
                MinAttribute = r.MinAttribute,
                IsOptional = r.IsOptional,
                Description = r.Description
            }).ToList()
        };
        return await _bossRepository.CreateTemplateAsync(template);
    }

    public async Task UpdateTemplateAsync(int id, BossTemplateRequest request)
    {
        var template = new BossTemplate
        {
            Id = id,
            BossId = request.BossId,
            Name = request.Name,
            Requirements = request.Requirements.Select(r => new BossTemplateRequirement
            {
                JobCategory = r.JobCategory,
                Count = r.Count,
                Priority = r.Priority,
                MinLevel = r.MinLevel,
                MinAttribute = r.MinAttribute,
                IsOptional = r.IsOptional,
                Description = r.Description
            }).ToList()
        };
        var ok = await _bossRepository.UpdateTemplateAsync(template);
        if (!ok) throw new NotFoundException($"BossTemplate {id} not found");
    }

    public async Task DeleteTemplateAsync(int templateId)
    {
        var ok = await _bossRepository.DeleteTemplateAsync(templateId);
        if (!ok) throw new NotFoundException($"BossTemplate {templateId} not found");
    }

    public async Task<int> CreateBossAsync(BossRequest request)
    {
        var boss = new Boss
        {
            Name = request.Name,
            RequireMembers = request.RequireMembers,
            RoundConsumption = request.RoundConsumption
        };
        return await _bossRepository.CreateBossAsync(boss);
    }

    public async Task UpdateBossAsync(int id, BossRequest request)
    {
        var boss = new Boss
        {
            Id = id,
            Name = request.Name,
            RequireMembers = request.RequireMembers,
            RoundConsumption = request.RoundConsumption
        };
        var ok = await _bossRepository.UpdateBossAsync(boss);
        if (!ok) throw new NotFoundException($"Boss {id} not found");
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
