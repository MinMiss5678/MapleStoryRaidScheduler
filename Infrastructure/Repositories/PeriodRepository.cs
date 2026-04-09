using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;

namespace Infrastructure.Repositories;

public class PeriodRepository : IPeriodRepository
{
    private readonly DbContext _dbContext;

    public PeriodRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CreateAsync(Period period)
    {
        await _dbContext.Repository<PeriodDbModel>().InsertAsync(new PeriodDbModel()
        {
            StartDate = period.StartDate,
            EndDate = period.EndDate
        });
    }

    public async Task<bool> ExistByStartDateAsync(DateTimeOffset startDate)
    {
        return await _dbContext.Repository<PeriodDbModel>().ExistAsync(x=>x.StartDate == startDate);
    }
}