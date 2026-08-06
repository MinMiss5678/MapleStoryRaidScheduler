using System.Text.Json;
using Application.Events;
using Application.Interface;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// 處理 <see cref="OutboxEventType.TeamNotification"/>：對指定玩家發 Discord DM（leader-led §11）。
/// 只註冊在 bot 行程（DiscordClient 在那）——正是 outbox 要跨的行程界線（寫在 API、送在 bot）。
/// 冪等：重送頂多多發一則相同 DM（可接受）。
/// </summary>
public class TeamNotificationOutboxHandler : IOutboxHandler
{
    private readonly IDiscordService _discordService;
    private readonly ILogger<TeamNotificationOutboxHandler> _logger;

    public TeamNotificationOutboxHandler(IDiscordService discordService, ILogger<TeamNotificationOutboxHandler> logger)
    {
        _discordService = discordService;
        _logger = logger;
    }

    public string Type => OutboxEventType.TeamNotification;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var e = JsonSerializer.Deserialize<TeamNotificationEvent>(payload)
                ?? throw new InvalidOperationException("TeamNotification payload 解析失敗");

        try
        {
            await _discordService.SendDirectMessageAsync(e.TargetDiscordId, e.Message);
        }
        catch (UnauthorizedException)
        {
            // 對方關閉「接收伺服器成員 DM」→ 永久失敗：不 rethrow，讓 outbox 標 processed、不重試（避免毒訊息）。
            // 站內「我的邀請/我的隊」清單仍是權威真相（§11），玩家登入照樣看得到，不漏。
            _logger.LogInformation("玩家 {Id} 關閉 DM，通知略過", e.TargetDiscordId);
        }
        catch (NotFoundException)
        {
            // 已退公會（bot 只能私訊同公會者）→ 永久失敗，同樣吞掉不重試。
            _logger.LogInformation("玩家 {Id} 不在公會，通知略過", e.TargetDiscordId);
        }
        // 其餘例外（網路、429 限流等暫時失敗）→ 讓它 throw → outbox 重試（暫時錯才該重試）。
    }
}
