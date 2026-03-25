using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Query;

public class PeriodQuery : IPeriodQuery
{
    private readonly IUnitOfWork _unitOfWork;

    public PeriodQuery(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }
    
    public async Task<int> GetPeriodIdByNowAsync()
    {
        var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);

// 計算本週四
        var daysUntilNextThursday  = ((int)DayOfWeek.Thursday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilNextThursday == 0) daysUntilNextThursday = 7;
        var nextThursday = today.AddDays(daysUntilNextThursday);
// 下週三
        var nextWednesday = nextThursday.AddDays(6);
        
        var sql = new QueryBuilder();
        sql.Select<PeriodDbModel>(x => new {x.Id})
            .From<PeriodDbModel>()
            .Where<PeriodDbModel>(x => x.StartDate >= nextThursday && x.StartDate <= nextWednesday);
        var periodId = await _unitOfWork.QuerySingleOrDefaultAsync<int?>(sql);

        return await _unitOfWork.QuerySingleAsync<int>(sql);
    }

    public async Task<Period> GetByNowAsync()
    {
        var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);

// 計算本週四
        var daysUntilNextThursday  = ((int)DayOfWeek.Thursday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilNextThursday == 0) daysUntilNextThursday = 7;
        var nextThursday = today.AddDays(daysUntilNextThursday);
// 下週三
        var nextWednesday = nextThursday.AddDays(6);
        
        var sql = new QueryBuilder();
        sql.Select<PeriodDbModel>(x => new
            {
                x.Id,
                x.StartDate,
                x.EndDate
            })
            .From<PeriodDbModel>()
            .Where<PeriodDbModel>(x => x.StartDate >= nextThursday && x.StartDate <= nextWednesday);
        
        return await _unitOfWork.QuerySingleAsync<Period>(sql);
    }
}