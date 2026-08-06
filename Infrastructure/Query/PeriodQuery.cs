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

    public async Task<int> GetActivePeriodIdAsync()
    {
        var period = await GetActivePeriodAsync();
        return period?.Id ?? 0;
    }

    public async Task<int> GetPeriodIdByDateAsync(DateTimeOffset date)
    {
        // 使用 UTC 日期，避免帶 offset 的輸入（如 +08:00）導致日期偏移一天
        var targetDate = new DateTimeOffset(date.UtcDateTime.Date, TimeSpan.Zero);
        var sql = new QueryBuilder();
        sql.Select<PeriodDbModel>(x => new { x.Id })
            .From<PeriodDbModel>()
            .Where<PeriodDbModel>(x => x.StartDate <= targetDate && x.EndDate >= targetDate)
            // 可能有多個 period 涵蓋同一日期（rolling-week 相鄰 period 邊界重疊、或環境殘留舊 period）：
            // 取 StartDate 最新的那個當「當前」，與 GetActivePeriodAsync 慣例一致，且避免 Single 撞多筆炸 500。
            .OrderByDescending<PeriodDbModel>(x => x.StartDate)
            .Limit(1);

        // 已 Limit(1) → 至多一列，QuerySingleOrDefault 安全（不會再撞多筆）。
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

    public async Task<Period?> GetActivePeriodAsync()
    {
        // 永遠回傳 DB 最新建立的 period（即玩家正在報名或即將出戰的那週）
        // WeeklyPeriodJob 每週四建立下一個 period，建立後自動切換
        var sql = new QueryBuilder();
        sql.Select<PeriodDbModel>(x => new
        {
            x.Id,
            x.StartDate,
            x.EndDate
        })
            .From<PeriodDbModel>()
            .OrderByDescending<PeriodDbModel>(x => x.StartDate)
            .Limit(1);

        return await _dbContext.QuerySingleOrDefaultAsync<Period>(sql);
    }

    public async Task<Period?> GetNextPeriodAsync()
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
            .Where<PeriodDbModel>(x => x.StartDate > now)
            .OrderBy<PeriodDbModel>(x => x.StartDate)
            .Limit(1);

        return await _dbContext.QuerySingleOrDefaultAsync<Period>(sql);
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
