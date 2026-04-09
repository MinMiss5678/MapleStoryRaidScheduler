using Domain.Entities;

namespace Domain.Repositories;

public interface IPlayerRepository
{
    Task<int> CreateAsync(Player player);
    Task<bool> ExistAsync(ulong discordId);
    Task<Player?> GetAsync(ulong discordId);
    Task<int> UpdateRoleAsync(ulong discordId, string role);
}