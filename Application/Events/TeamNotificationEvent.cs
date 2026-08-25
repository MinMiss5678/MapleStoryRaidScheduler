namespace Application.Events;

/// <summary>
/// leader-led 組隊通知的 outbox payload（§11）：對 <see cref="TargetDiscordId"/> 發一則 Discord DM。
/// 訊息在寫入端（service，有王名/時段等 context）就組好；handler 只負責送，不需再查。
/// </summary>
public class TeamNotificationEvent
{
    public ulong TargetDiscordId { get; set; }
    public string Message { get; set; } = "";

    /// <summary>
    /// 這則通知是否附「可在 Discord 內直接操作」的按鈕（discord-inline-actions）。
    /// <see cref="TeamNotificationAction.None"/>（預設）＝純文字；bot 端 handler 依此決定要不要渲染按鈕、渲染哪組。
    /// 舊事件（無此欄位）反序列化為 None → 向後相容、走純文字路徑。
    /// </summary>
    public TeamNotificationAction Action { get; set; } = TeamNotificationAction.None;

    /// <summary>
    /// <see cref="Action"/> 非 None 時帶的「按鈕目標 Id」，供 bot 組 custom_id、點擊時走對應 service 方法。
    /// 意義依 Action 而定：<see cref="TeamNotificationAction.InviteResponse"/> / <see cref="TeamNotificationAction.ApplicationReview"/>
    /// ＝成員（TeamSlotCharacter）Id；<see cref="TeamNotificationAction.TransferResponse"/>＝隊伍（TeamSlot）Id。
    /// </summary>
    public int? ActionId { get; set; }
}

/// <summary>
/// 通知可帶的「Discord 內建動作」種類（discord-inline-actions）。每種對應一組「正向/負向」兩顆按鈕。
/// </summary>
public enum TeamNotificationAction
{
    /// <summary>純文字通知（無按鈕）。</summary>
    None = 0,

    /// <summary>邀請 → 玩家 DM 附「接受 / 拒絕」。ActionId＝memberId。</summary>
    InviteResponse = 1,

    /// <summary>有人申請 → 隊長 DM 附「核准 / 拒絕」。ActionId＝memberId。</summary>
    ApplicationReview = 2,

    /// <summary>轉讓邀你 → 新隊長候選 DM 附「接受 / 拒絕」。ActionId＝teamSlotId。</summary>
    TransferResponse = 3
}
