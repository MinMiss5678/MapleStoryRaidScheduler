using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

/// <summary>常設可用時段（period-less §8 Phase 2）：掛玩家（DiscordId），取代掛 register 的舊表。</summary>
public class PlayerAvailabilityStandingRepository : IPlayerAvailabilityStandingRepository
{
    private readonly DbContext _dbContext;

    public PlayerAvailabilityStandingRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CreateAsync(PlayerAvailability model)
    {
        await _dbContext.Repository<PlayerAvailabilityStandingDbModel>().InsertAsync(new PlayerAvailabilityStandingDbModel
        {
            DiscordId = (long)model.DiscordId,
            Weekday = model.Weekday,
            StartTime = model.StartTime,
            EndTime = model.EndTime
        });
    }

    public async Task DeleteByDiscordIdAsync(ulong discordId)
    {
        var sql = new DeleteBuilder<PlayerAvailabilityStandingDbModel>();
        sql.Where(x => x.DiscordId == (long)discordId);
        await _dbContext.ExecuteAsync(sql);
    }
}
