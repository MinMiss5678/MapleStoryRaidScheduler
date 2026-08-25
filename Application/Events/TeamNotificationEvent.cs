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

    /// <summary>
    /// 富呈現資料（bot-composed-embeds）：非 null 時 bot 用 embed 呈現（如邀請 DM 列出目前成員職業/攻擊力）。
    /// 入列時由 backend 撈好 roster 快照 denormalize 進來 → bot 純渲染、不查 DB。null → 走 <see cref="Message"/> 純文字（fallback）。
    /// </summary>
    public TeamEmbedData? Embed { get; set; }
}

/// <summary>邀請 DM 的 embed 快照資料（入列當下）：讓被邀玩家決定前看得到隊伍組成 + 自己被邀的角色。</summary>
public class TeamEmbedData
{
    public string BossName { get; set; } = "";
    public string TimeText { get; set; } = "";      // 已格式化（+8 時區 M/d HH:mm）
    public int Capacity { get; set; }               // Boss.RequireMembers

    // 這次動作對應的角色（邀請＝被邀角色、申請＝申請者角色）——讓對方看得到是哪隻、能力如何。
    public string SubjectName { get; set; } = "";
    public string SubjectJob { get; set; } = "";
    public int SubjectAttackPower { get; set; }
    public int SubjectLevel { get; set; }
    public int SubjectMapleBlessingLevel { get; set; }

    public List<RosterEntry> Roster { get; set; } = new();   // 目前已確認成員（快照）
}

/// <summary>roster 一列：職業 + 攻擊力 + 人物等級 + 楓葉祝福等級（戰力欄，不含身分，§9.12）。</summary>
public class RosterEntry
{
    public string Job { get; set; } = "";
    public int AttackPower { get; set; }
    public int Level { get; set; }
    public int MapleBlessingLevel { get; set; }
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
