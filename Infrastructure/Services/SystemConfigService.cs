using Application.Events;
using Application.Interface;
using Domain.Entities;
using Infrastructure.Dapper;
using Infrastructure.Entities;

namespace Infrastructure.Services;

public class SystemConfigService : ISystemConfigService
{
    private readonly DbContext _dbContext;
    private readonly IOutbox _outbox;

    public SystemConfigService(DbContext dbContext, IOutbox outbox)
    {
        _dbContext = dbContext;
        _outbox = outbox;
    }

    public async Task<SystemConfig> GetAsync()
    {
        var dbModel = (await _dbContext.Repository<SystemConfigDbModel>().GetAllAsync<SystemConfigDbModel>())
            .FirstOrDefault();

        if (dbModel == null)
        {
            // 預設截止日設在重製日前一天（重製=週二 → 截止週一 23:59:59）
            return new SystemConfig
            {
                Id = 1,
                DeadlineDayOfWeek = DayOfWeek.Monday,
                DeadlineTime = new TimeSpan(23, 59, 59),
                IsDeadlineNotified = false
            };
        }

        return new SystemConfig
        {
            Id = dbModel.Id,
            DeadlineDayOfWeek = (DayOfWeek)dbModel.DeadlineDayOfWeek,
            DeadlineTime = dbModel.DeadlineTime,
            IsDeadlineNotified = dbModel.IsDeadlineNotified
        };
    }

    public async Task UpdateAsync(SystemConfig config)
    {
        var repository = _dbContext.Repository<SystemConfigDbModel>();
        var existing = (await repository.GetAllAsync<SystemConfigDbModel>()).FirstOrDefault();

        if (existing == null)
        {
            await repository.InsertAsync(new SystemConfigDbModel
            {
                DeadlineDayOfWeek = (int)config.DeadlineDayOfWeek,
                DeadlineTime = config.DeadlineTime,
                IsDeadlineNotified = config.IsDeadlineNotified
            });
        }
        else
        {
            // 如果期限有變動，重置通知狀態
            if (existing.DeadlineDayOfWeek != (int)config.DeadlineDayOfWeek ||
                existing.DeadlineTime != config.DeadlineTime)
            {
                existing.IsDeadlineNotified = false;
            }
            else
            {
                existing.IsDeadlineNotified = config.IsDeadlineNotified;
            }

            existing.DeadlineDayOfWeek = (int)config.DeadlineDayOfWeek;
            existing.DeadlineTime = config.DeadlineTime;
            await repository.UpdateAsync(existing);
        }

        // 設定變更事件走 transactional outbox：把事件寫進「與本次 UPDATE 同一筆交易」的 outbox 列
        //  → 與資料原子提交/回滾（rollback 就不會有鬼影事件）。
        // 取代原本的 in-process AfterCommit：那個 (1) commit 後 crash 會遺失、(2) 跨不了行程
        //  （設定在 API 改、喚醒的 job 在 bot）。outbox 由 bot 的 OutboxDispatcher 讀已提交列去投遞。
        await _outbox.EnqueueAsync(OutboxEventType.ConfigChanged, config);
    }
}
