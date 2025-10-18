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
            var now = DateTimeOffset.Now;

            // 計算下一個週四 00:00
            int daysUntilThursday = ((int)DayOfWeek.Thursday - (int)now.DayOfWeek + 7) % 7;
            if (daysUntilThursday == 0 && now.TimeOfDay.TotalHours >= 0) 
                daysUntilThursday = 7; // 如果今天已經是週四 00:00 之後，就跳到下週四

            var nextThursday = now.Date.AddDays(daysUntilThursday); // 00:00
            var delay = nextThursday - now;

            _logger.LogInformation($"Next weekly period job will run in {delay.TotalMinutes:F0} minutes.");

            // 等待到下一個週四
            await Task.Delay(delay, stoppingToken);

            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                var start = nextThursday;
                var end = start.AddDays(6).AddHours(23).AddMinutes(59).AddSeconds(59);
                
                using var scope = _serviceProvider.CreateScope();
                var repo  = scope.ServiceProvider.GetRequiredService<IPeriodRepository>();

                if (!await repo.ExistByStartDateAsync(start))
                {
                    await repo.CreateAsync(new Period
                    {
                        StartDate = start,
                        EndDate = end
                    });
                }

                _logger.LogInformation($"Inserted period: {start:yyyy-MM-dd} ~ {end:yyyy-MM-dd}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting weekly period");
            }
        }
    }
}