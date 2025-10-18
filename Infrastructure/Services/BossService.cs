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
}