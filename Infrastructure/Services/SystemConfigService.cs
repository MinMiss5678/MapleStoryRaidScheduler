using Application.Events;
using Application.Interface;
using Domain.Entities;
using Infrastructure.Dapper;
using Infrastructure.Entities;

namespace Infrastructure.Services;

public class SystemConfigService : ISystemConfigService
{
    private readonly DbContext _dbContext;
    private readonly ConfigChangeNotifier _notifier;

    public SystemConfigService(DbContext dbContext, ConfigChangeNotifier notifier)
    {
        _dbContext = dbContext;
        _notifier = notifier;
    }

    public async Task<SystemConfig> GetAsync()
    {
        var dbModel = (await _dbContext.Repository<SystemConfigDbModel>().GetAllAsync<SystemConfigDbModel>())
            .FirstOrDefault();

        if (dbModel == null)
        {
            // 預設給一個時間，例如週三 23:59:59
            return new SystemConfig
            {
                Id = 1,
                DeadlineDayOfWeek = DayOfWeek.Wednesday,
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

        _notifier.Notify();
    }
}
