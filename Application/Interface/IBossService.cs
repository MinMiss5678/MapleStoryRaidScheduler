using Application.DTOs;
using Domain.Entities;

namespace Application.Interface;

public interface IBossService
{
    Task<IEnumerable<Boss>> GetAllAsync();
    Task<int> CreateBossAsync(BossRequest request);
    Task UpdateBossAsync(int id, BossRequest request);
    Task DeleteBossAsync(int bossId);
}
