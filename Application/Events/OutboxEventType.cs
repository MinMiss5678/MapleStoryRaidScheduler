namespace Application.Events;

/// <summary>Outbox 事件類型常數——寫入端與 handler 共用同一組字串，避免手打字串不一致。</summary>
public static class OutboxEventType
{
    /// <summary>系統設定（報名截止）變更 → 喚醒 bot 的 RegistrationDeadlineJob 重讀重算。</summary>
    public const string ConfigChanged = "ConfigChanged";

    /// <summary>leader-led 組隊通知（邀請/申請/核准…）→ bot 對指定玩家發 Discord DM。payload = TeamNotificationEvent。</summary>
    public const string TeamNotification = "TeamNotification";
}
