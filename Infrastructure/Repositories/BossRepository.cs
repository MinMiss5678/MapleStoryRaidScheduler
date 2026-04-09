using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;

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
            x.RequireMembers
        });
    }
}