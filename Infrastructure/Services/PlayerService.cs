using Application.Interface;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Services;

public class PlayerService : IPlayerService
{
    private readonly IPlayerRepository _playerRepository;

    public PlayerService(IPlayerRepository playerRepository)
    {
        _playerRepository = playerRepository;
    }

    public async Task CreateAsync(Player player)
    {
        // repo 現為 upsert：既有玩家會更新 DiscordName（公會暱稱重登刷新），故不再 check-then-insert。
        await _playerRepository.CreateAsync(player);
    }

    public async Task<Player?> GetAsync(ulong discordId)
    {
        return await _playerRepository.GetAsync(discordId);
    }

    public async Task UpdateRoleAsync(ulong discordId, string role)
    {
        await _playerRepository.UpdateRoleAsync(discordId, role);
    }
}
