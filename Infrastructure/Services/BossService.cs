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
        var ok = await _bossRepository.DeleteBossAsync(bossId);
        if (!ok) throw new NotFoundException($"Boss {bossId} not found");
    }
}
