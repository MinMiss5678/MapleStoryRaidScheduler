using Application.Interface;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Entities;

namespace Infrastructure.Repositories;

public class PlayerRepository : IPlayerRepository
{
    private readonly IUnitOfWork _unitOfWork;

    public PlayerRepository(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> ExistAsync(ulong discordId)
    {
        return await _unitOfWork.Repository<PlayerDbModel>().ExistAsync((long)discordId);
    }

    public async Task<int> CreateAsync(Player player)
    {
        return await _unitOfWork.Repository<PlayerDbModel>().InsertAsync(new PlayerDbModel()
        {
            DiscordId = (long)player.DiscordId,
            DiscordName = player.DiscordName
        });
    }
}