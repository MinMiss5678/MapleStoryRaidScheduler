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
        if (!await _playerRepository.ExistAsync(player.DiscordId))
        {
            await _playerRepository.CreateAsync(player);
        }
    }
}