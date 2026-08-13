using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class BossRepository : IBossRepository
{
    private readonly DbContext _dbContext;

    public BossRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<Boss>> GetAllAsync()
    {
        return await _dbContext.Repository<BossDbModel>().GetAllAsync<Boss>(x => new
        {
            x.Id,
            x.Name,
            x.RequireMembers,
            x.RoundConsumption
        });
    }

    public async Task<Boss?> GetByIdAsync(int bossId)
    {
        var sql = new QueryBuilder()
            .Select<BossDbModel>(x => new { x.Id, x.Name, x.RequireMembers, x.RoundConsumption })
            .From<BossDbModel>()
            .Where<BossDbModel>(x => x.Id == bossId);

        return await _dbContext.QuerySingleOrDefaultAsync<Boss>(sql);
    }

    public async Task<int> CreateBossAsync(Boss boss)
    {
        var sql = new InsertBuilder<BossDbModel>()
            .Set(x => x.Name, boss.Name)
            .Set(x => x.RequireMembers, boss.RequireMembers)
            .Set(x => x.RoundConsumption, boss.RoundConsumption)
            .ReturnId();
        return await _dbContext.ExecuteScalarAsync(sql);
    }

    public async Task<bool> UpdateBossAsync(Boss boss)
    {
        var sql = new UpdateBuilder<BossDbModel>()
            .Set(x => x.Name, boss.Name)
            .Set(x => x.RequireMembers, boss.RequireMembers)
            .Set(x => x.RoundConsumption, boss.RoundConsumption)
            .Where(x => x.Id == boss.Id);
        var result = await _dbContext.ExecuteAsync(sql);
        return result > 0;
    }

    public async Task<bool> DeleteBossAsync(int bossId)
    {
        var sql = new DeleteBuilder<BossDbModel>()
            .Where(x => x.Id == bossId);
        var result = await _dbContext.ExecuteAsync(sql);
        return result > 0;
    }
}
