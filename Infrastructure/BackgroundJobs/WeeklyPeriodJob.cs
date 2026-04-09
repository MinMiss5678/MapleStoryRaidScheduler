using Domain.Entities;
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

            // 計算最近的週四 00:00 UTC（今天是週四且已過 00:00，會取下週週四）
            int daysUntilNextThursday = ((int)DayOfWeek.Thursday - (int)now.DayOfWeek + 7) % 7;
            if (daysUntilNextThursday == 0) daysUntilNextThursday = 7; // 今天週四已過，跳到下週
            var nextThursday = new DateTimeOffset(now.Date.AddDays(daysUntilNextThursday), TimeSpan.Zero);

            // 計算當週週期的起訖時間
            var periodStart = nextThursday;
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

            // 計算下次延遲到下一個週四 00:00
            var delay = nextThursday.AddDays(7) - DateTimeOffset.UtcNow;
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