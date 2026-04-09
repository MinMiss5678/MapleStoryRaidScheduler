using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class TeamSlotRepository : ITeamSlotRepository
{
    private readonly DbContext _dbContext;

    public TeamSlotRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> CreateAsync(TeamSlot teamSlot)
    {
        var sql = new InsertBuilder<TeamSlotDbModel>();
        sql.Set(x => x.BossId, teamSlot.BossId)
            .Set(x => x.SlotDateTime, teamSlot.SlotDateTime)
            .ReturnId();

        return await _dbContext.ExecuteScalarAsync(sql);
    }

    public async Task DeleteAsync(int id)
    {
        await _dbContext.Repository<TeamSlotDbModel>().DeleteAsync(id);
    }
}