using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class PlayerAvailabilityRepository : IPlayerAvailabilityRepository
{
    private readonly DbContext _dbContext;

    public PlayerAvailabilityRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CreateAsync(PlayerAvailability model)
    {
        await _dbContext.Repository<PlayerAvailabilityDbModel>().InsertAsync(new PlayerAvailabilityDbModel
        {
            PlayerRegisterId = model.PlayerRegisterId,
            Weekday = model.Weekday,
            StartTime = model.StartTime,
            EndTime = model.EndTime
        });
    }

    public async Task DeleteByPlayerRegisterIdAsync(int playerRegisterId)
    {
        var sql = new DeleteBuilder<PlayerAvailabilityDbModel>();
        sql.Where(x => x.PlayerRegisterId == playerRegisterId);
        await _dbContext.ExecuteAsync(sql);
    }

    public async Task<IEnumerable<PlayerAvailability>> GetByPlayerRegisterIdAsync(int playerRegisterId)
    {
        var sql = new QueryBuilder();
        sql.Select<PlayerAvailabilityDbModel>(x => new
            {
                x.Id,
                x.PlayerRegisterId,
                x.Weekday,
                x.StartTime,
                x.EndTime
            })
            .From<PlayerAvailabilityDbModel>()
            .Where<PlayerAvailabilityDbModel>(x => x.PlayerRegisterId == playerRegisterId);
        return await _dbContext.QueryAsync<PlayerAvailability>(sql);
    }

    public async Task<IEnumerable<PlayerAvailability>> GetByDiscordIdAndPeriodIdAsync(ulong discordId, int periodId)
    {
        var sql = new QueryBuilder();
        sql.Select<PlayerAvailabilityDbModel>(x => new
            {
                x.Id,
                x.PlayerRegisterId,
                x.Weekday,
                x.StartTime,
                x.EndTime
            })
            .Select<PlayerRegisterDbModel>(x => new
            {
                x.DiscordId
            }, "b")
            .From<PlayerAvailabilityDbModel>()
            .LeftJoin<PlayerRegisterDbModel>("""
                                              a."PlayerRegisterId" = b."Id"
                                              """)
            .Where<PlayerRegisterDbModel>(x => x.DiscordId == (long)discordId && x.PeriodId == periodId);

        return await _dbContext.QueryAsync<PlayerAvailability>(sql);
    }
    
    public async Task<IEnumerable<PlayerAvailability>> GetByDiscordIdsAndPeriodIdAsync(List<ulong> discordIds, int periodId)
    {
        var sql = new QueryBuilder();
        sql.Select<PlayerAvailabilityDbModel>(x => new
            {
                x.Id,
                x.PlayerRegisterId,
                x.Weekday,
                x.StartTime,
                x.EndTime
            })
            .Select<PlayerRegisterDbModel>(x => new
            {
                x.DiscordId
            }, "b")
            .From<PlayerAvailabilityDbModel>()
            .LeftJoin<PlayerRegisterDbModel>("""
                                             a."PlayerRegisterId" = b."Id"
                                             """)
            .Where<PlayerRegisterDbModel>(x => discordIds.Select(q => (long)q).ToList()
                .Contains(x.DiscordId) && x.PeriodId == periodId);

        return await _dbContext.QueryAsync<PlayerAvailability>(sql);
    }
}