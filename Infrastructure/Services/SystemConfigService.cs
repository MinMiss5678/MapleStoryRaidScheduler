using Application.Interface;
using Domain.Entities;
using Infrastructure.Dapper;
using Infrastructure.Entities;

namespace Infrastructure.Services;

public class SystemConfigService : ISystemConfigService
{
    private readonly DbContext _dbContext;

    public SystemConfigService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SystemConfig> GetAsync()
    {
        var dbModel = (await _dbContext.Repository<SystemConfigDbModel>().GetAllAsync<SystemConfigDbModel>())
            .FirstOrDefault();

        if (dbModel == null)
        {
            return new SystemConfig { Id = 1 };
        }

        return new SystemConfig
        {
            Id = dbModel.Id,
            LeaveRateWarnEnabled = dbModel.LeaveRateWarnEnabled,
            LeaveRateWindowMonths = dbModel.LeaveRateWindowMonths,
            LeaveRateThreshold = dbModel.LeaveRateThreshold,
            LeaveRateMinSample = dbModel.LeaveRateMinSample
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
                LeaveRateWarnEnabled = config.LeaveRateWarnEnabled,
                LeaveRateWindowMonths = config.LeaveRateWindowMonths,
                LeaveRateThreshold = config.LeaveRateThreshold,
                LeaveRateMinSample = config.LeaveRateMinSample
            });
        }
        else
        {
            existing.LeaveRateWarnEnabled = config.LeaveRateWarnEnabled;
            existing.LeaveRateWindowMonths = config.LeaveRateWindowMonths;
            existing.LeaveRateThreshold = config.LeaveRateThreshold;
            existing.LeaveRateMinSample = config.LeaveRateMinSample;
            await repository.UpdateAsync(existing);
        }

        // 設定即時寫 DB；讀取端（TeamLeaderService.GetCandidatesAsync）每次直接讀最新值，
        // 無記憶體快取要失效、無背景 job 要喚醒 → 不需要跨行程通知。
        // （原本的 ConfigChanged outbox 是給已退場的報名截止 job 用的，period-less 後拔除。）
    }
}
