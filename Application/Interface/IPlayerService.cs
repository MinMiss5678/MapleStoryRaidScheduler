using Domain.Entities;

namespace Application.Interface;

public interface IPlayerService
{
    Task CreateAsync(Player player);
    Task<Player?> GetAsync(ulong discordId);
    Task UpdateRoleAsync(ulong discordId, string role);
}