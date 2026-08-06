namespace Application.Events;

/// <summary>
/// leader-led 組隊通知的 outbox payload（§11）：對 <see cref="TargetDiscordId"/> 發一則 Discord DM。
/// 訊息在寫入端（service，有王名/時段等 context）就組好；handler 只負責送，不需再查。
/// </summary>
public class TeamNotificationEvent
{
    public ulong TargetDiscordId { get; set; }
    public string Message { get; set; } = "";
}
