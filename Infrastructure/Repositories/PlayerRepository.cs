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
            DiscordName = player.DiscordName,
            Role = player.Role
        });
    }

    public async Task<Player?> GetAsync(ulong discordId)
    {
        var player = await _dbContext.Repository<PlayerDbModel>().GetByIdAsync((long)discordId);
        if (player == null) return null;

        return new Player()
        {
            DiscordId = (ulong)player.DiscordId,
            DiscordName = player.DiscordName,
            Role = player.Role
        };
    }

    public async Task<int> UpdateRoleAsync(ulong discordId, string role)
    {
        const string sql = "UPDATE \"Player\" SET \"Role\"=@Role WHERE \"DiscordId\"=@DiscordId";
        return await _dbContext.ExecuteAsync(sql, new { Role = role, DiscordId = (long)discordId });
    }
}