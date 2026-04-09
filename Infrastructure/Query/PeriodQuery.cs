using Application.Queries;
using Domain.Entities;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Query;

public class PeriodQuery : IPeriodQuery
{
    private readonly DbContext _dbContext;

    public PeriodQuery(DbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    public async Task<int> GetPeriodIdByNowAsync()
    {
        var period = await GetByNowAsync();
        return period?.Id ?? 0;
    }

    public async Task<int> GetPeriodIdByDateAsync(DateTimeOffset date)
    {
        var targetDate = new DateTimeOffset(date.Date, TimeSpan.Zero);
        var sql = new QueryBuilder();
        sql.Select<PeriodDbModel>(x => new { x.Id })
            .From<PeriodDbModel>()
            .Where<PeriodDbModel>(x => x.StartDate <= targetDate && x.EndDate >= targetDate);

        var periodId = await _dbContext.QuerySingleOrDefaultAsync<int?>(sql);
        return periodId ?? 0;
    }

    public async Task<int> GetLastPeriodIdAsync()
    {
        var sql = new QueryBuilder();
        sql.Select<PeriodDbModel>(x => new { x.Id })
            .From<PeriodDbModel>()
            .OrderByDescending<PeriodDbModel>(x => x.StartDate)
            .Offset(1) // 跳過最新的一個（當前/下一個）
            .Limit(1);

        var periodId = await _dbContext.QuerySingleOrDefaultAsync<int?>(sql);
        return periodId ?? 0;
    }

    public async Task<Period?> GetByNowAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var sql = new QueryBuilder();
        sql.Select<PeriodDbModel>(x => new
            {
                x.Id,
                x.StartDate,
                x.EndDate
            })
            .From<PeriodDbModel>()
            .Where<PeriodDbModel>(x => x.StartDate <= now)
            .OrderByDescending<PeriodDbModel>(x => x.StartDate)
            .Limit(1);
        
        return  await _dbContext.QuerySingleOrDefaultAsync<Period>(sql);
    }

    public async Task<Period?> GetByIdAsync(int id)
    {
        var sql = new QueryBuilder();
        sql.Select<PeriodDbModel>(x => new
            {
                x.Id,
                x.StartDate,
                x.EndDate
            })
            .From<PeriodDbModel>()
            .Where<PeriodDbModel>(x => x.Id == id);

        return await _dbContext.QuerySingleOrDefaultAsync<Period?>(sql);
    }
}