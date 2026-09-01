using Application.Events;
using Application.Interface;
using Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// 常設可用時段新鮮度「快過期」提醒（階段二，plans/2026-09-01-availability-freshness-decay.md）。
/// 定時撈「參戰中、快過期、且『上次提醒後又有活動』」的玩家 → enqueue FreshnessNudge outbox（DM 附「留任／移除我」）
/// + 標記已提醒。enqueue ＋ 標記**同交易**（原子：避免送了沒標／標了沒送）。
///
/// 只註冊在 bot（單一實例）→ 無多 pod 重複提醒（同 OutboxRetentionJob/LfgIntentCleanupJob 慣例）。
/// BackgroundService 是 Singleton，故每輪 <see cref="IServiceScopeFactory.CreateScope"/> 取 scoped UoW/repo（bot-di-scoping）。
/// 門檻由 admin 設定（<c>SystemConfig.AvailabilityFreshnessDays</c>）；前置天 <see cref="LeadDays"/> 為常數初值。
/// </summary>
public class AvailabilityFreshnessNudgeJob : BackgroundService
{
    private const int LeadDays = 3;                                   // 門檻前幾天發提醒（初值；計畫未解，之後可調）
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AvailabilityFreshnessNudgeJob> _logger;

    public AvailabilityFreshnessNudgeJob(IServiceScopeFactory scopeFactory, ILogger<AvailabilityFreshnessNudgeJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AvailabilityFreshnessNudgeJob is starting.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var n = await RunOnceAsync(stoppingToken);
                if (n > 0) _logger.LogInformation("Freshness nudge：enqueued {Count} 則提醒", n);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "AvailabilityFreshnessNudgeJob run failed"); }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    // internal：供整合測確定性地跑一次（不靠 6 小時輪詢）。回傳本輪 enqueue 的提醒數。
    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var outbox = sp.GetRequiredService<IOutbox>();
        var players = sp.GetRequiredService<IPlayerRepository>();
        var config = sp.GetRequiredService<ISystemConfigService>();

        var thresholdDays = (await config.GetAsync()).AvailabilityFreshnessDays;
        var nudgeAfterDays = Math.Max(1, thresholdDays - LeadDays);   // threshold ≤ LeadDays 時夾在 ≥1

        await uow.BeginAsync();
        try
        {
            var targets = await players.GetFreshnessNudgeTargetsAsync(nudgeAfterDays);
            foreach (var discordId in targets)
            {
                await outbox.EnqueueAsync(OutboxEventType.TeamNotification, new TeamNotificationEvent
                {
                    TargetDiscordId = discordId,
                    Action = TeamNotificationAction.FreshnessNudge,
                    ActionId = 0,   // 對象＝點擊者本人，id 不帶意義
                    Message =
                        "⏰ 你太久沒在這裡開團／找團了，常設可用時段即將從找團名單淡出。\n" +
                        "想留在名單就按「留任」；不想被揪按「移除我」（你填的可用時段會保留，隨時可再開啟）。"
                });
                await players.MarkFreshnessNudgedAsync(discordId);
            }
            await uow.CommitAsync();
            return targets.Count;
        }
        catch
        {
            await uow.RollbackAsync();
            throw;
        }
    }
}
