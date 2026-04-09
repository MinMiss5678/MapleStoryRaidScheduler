using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;

namespace Infrastructure.Repositories;

public class PlayerRepository : IPlayerRepository
{
    private readonly DbContext _dbContext;

    public PlayerRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ExistAsync(ulong discordId)
    {
        return await _dbContext.Repository<PlayerDbModel>().ExistAsync((long)discordId);
    }

    public async Task<int> CreateAsync(Player player)
    {
        return await _dbContext.Repository<PlayerDbModel>().InsertAsync(new PlayerDbModel()
        {
            DiscordId = (long)player.DiscordId,
            DiscordName = player.DiscordName
        });
    }
}