using Application.Interface;
using Domain.Entities;
using Domain.Helpers;
using Domain.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.BackgroundJobs;

public class WeeklyPeriodJob : BackgroundService
{
    private readonly ILogger<WeeklyPeriodJob> _logger;
    private readonly IServiceProvider _serviceProvider;

    public WeeklyPeriodJob(ILogger<WeeklyPeriodJob> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;

            // 計算最近的重製日 00:00 UTC（今天就是重製日且已過 00:00，會取下週）
            var periodStart = SlotDateCalculator.NextReset(now);

            // 計算當週週期的起訖時間
            var periodEnd = periodStart.AddDays(6).AddHours(23).AddMinutes(59).AddSeconds(59);

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IPeriodRepository>();

                if (!await repo.ExistByStartDateAsync(periodStart))
                {
                    await repo.CreateAsync(new Period
                    {
                        StartDate = periodStart,
                        EndDate = periodEnd
                    });
                    _logger.LogInformation($"Inserted missing period: {periodStart:yyyy-MM-dd} ~ {periodEnd:yyyy-MM-dd}");

                    // 新週期建立後，重置截止通知旗標，讓 RegistrationDeadlineJob 為新週期發送通知
                    var configService = scope.ServiceProvider.GetRequiredService<ISystemConfigService>();
                    var config = await configService.GetAsync();
                    config.IsDeadlineNotified = false;
                    await configService.UpdateAsync(config);
                    _logger.LogInformation("Reset IsDeadlineNotified for new period.");
                }
                else
                {
                    _logger.LogInformation($"Period already exists: {periodStart:yyyy-MM-dd} ~ {periodEnd:yyyy-MM-dd}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting weekly period");
            }

            // 計算下次延遲到下一個重製日 00:00
            var delay = periodStart.AddDays(7) - DateTimeOffset.UtcNow;
            _logger.LogInformation($"Next weekly period job will run in {delay.TotalMinutes:F0} minutes.");

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
