using Application.Events;
using Application.Interface;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// 處理 <see cref="OutboxEventType.ConfigChanged"/>：喚醒本行程的 RegistrationDeadlineJob 重讀 config 重算截止。
/// 冪等：只是觸發 <see cref="ConfigChangeNotifier"/> 讓 job 重讀——重送一次頂多多讀一次，無副作用。
/// 只註冊在 bot（訂閱 notifier 的 job 在 bot）；這正是 outbox 要跨的行程界線（寫在 API、消費在 bot）。
/// </summary>
public class ConfigChangedOutboxHandler : IOutboxHandler
{
    private readonly ConfigChangeNotifier _notifier;

    public ConfigChangedOutboxHandler(ConfigChangeNotifier notifier)
    {
        _notifier = notifier;
    }

    public string Type => OutboxEventType.ConfigChanged;

    public Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        // payload（變更後的 config JSON）此處用不到——喚醒後 job 會自己重讀最新 config。
        _notifier.Notify();
        return Task.CompletedTask;
    }
}
