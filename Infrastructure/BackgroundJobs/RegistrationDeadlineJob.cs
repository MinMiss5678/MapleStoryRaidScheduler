using Application.Events;
using Application.Interface;
using Application.Options;
using Application.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.BackgroundJobs;

public class RegistrationDeadlineJob : BackgroundService
{
    private readonly ILogger<RegistrationDeadlineJob> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConfigChangeNotifier _notifier;
    private readonly AppOptions _appOptions;
    private CancellationTokenSource? _changeCts;

    public RegistrationDeadlineJob(ILogger<RegistrationDeadlineJob> logger, IServiceProvider serviceProvider, ConfigChangeNotifier notifier, IOptions<AppOptions> appOptions)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _notifier = notifier;
        _appOptions = appOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RegistrationDeadlineJob is starting.");

        _notifier.OnChanged += () =>
        {
            _logger.LogInformation("Configuration changed, restarting delay.");
            _changeCts?.Cancel();
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var configService = scope.ServiceProvider.GetRequiredService<ISystemConfigService>();
                var config = await configService.GetAsync();
                var now = DateTimeOffset.Now;

                if (!config.IsDeadlineNotified)
                {
                    var periodQuery = scope.ServiceProvider.GetRequiredService<IPeriodQuery>();
                    var currentPeriod = await periodQuery.GetByNowAsync();
                    
                    if (currentPeriod == null)
                    {
                        // 沒週期，一分鐘後檢查
                        delay = TimeSpan.FromMinutes(1);
                    }
                    else
                    {
                        var deadline = config.GetDeadlineForPeriod(currentPeriod.StartDate);
                        
                        if (now > deadline)
                        {
                            var discordService = scope.ServiceProvider.GetRequiredService<IDiscordService>();
                            
                            string periodInfo = $"{currentPeriod.StartDate:yyyy/MM/dd} ~ {currentPeriod.EndDate:yyyy/MM/dd}";

                            string message = $"📢 **報名已截止**\n\n" +
                                             $"本次週期：{periodInfo}\n" +
                                             $"排團結果查詢連結：{_appOptions.AppUrl}/scheduleResult\n" +
                                             $"請各位團員準時參加！";

                            await discordService.SendMessageAsync(message);
                            
                            config.IsDeadlineNotified = true;
                            await configService.UpdateAsync(config);
                            
                            _logger.LogInformation("Registration deadline notification sent.");
                            
                            // 通知發送後，直接等待到下一個週四 00:00 (WeeklyPeriodJob 重置時間)
                            delay = GetDelayUntilNextReset(now);
                        }
                        else
                        {
                            // 還沒到截止時間，計算到截止時間的剩餘時間
                            var timeToDeadline = deadline - now;
                            
                            // 最小檢查間隔 5 秒 (即將截止時)，最大檢查間隔為到截止時間
                            if (timeToDeadline < TimeSpan.FromMinutes(1))
                            {
                                delay = timeToDeadline > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : timeToDeadline.Add(TimeSpan.FromSeconds(1));
                            }
                            else
                            {
                                // 距離還遠，直接等待到截止時間
                                // 加上一點 Buffer 確保真的過了截止時間
                                delay = timeToDeadline.Add(TimeSpan.FromSeconds(5));
                            }
                        }
                    }
                }
                else
                {
                    // 已發送過通知，直接等待到下一個週四 00:00 (WeeklyPeriodJob 重置時間)
                    // 加上一點 Buffer 確保重置工作已經完成
                    delay = GetDelayUntilNextReset(now).Add(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking registration deadline");
                delay = TimeSpan.FromMinutes(1); // 發生錯誤時，1 分鐘後重試
            }

            _logger.LogInformation("RegistrationDeadlineJob will delay for {Delay}", delay);
            
            // 使用 Linked CancellationToken，當外部取消或設定更新時中斷等待
            _changeCts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _changeCts.Token);
            try
            {
                await Task.Delay(delay, linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (stoppingToken.IsCancellationRequested) break;
                // 若是由於 _changeCts 取消，則進入下一圈循環（重新讀取 config 並計算延遲）
            }
        }
    }

    private TimeSpan GetDelayUntilNextReset(DateTimeOffset now)
    {
        // 計算下一個週四 00:00 (與 WeeklyPeriodJob 邏輯一致)
        int daysUntilThursday = ((int)DayOfWeek.Thursday - (int)now.DayOfWeek + 7) % 7;
        if (daysUntilThursday == 0 && now.TimeOfDay.TotalHours >= 0)
            daysUntilThursday = 7;

        var nextThursday = now.Date.AddDays(daysUntilThursday);
        return nextThursday - now;
    }
}
