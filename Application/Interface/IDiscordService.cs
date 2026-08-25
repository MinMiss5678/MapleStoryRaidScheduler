namespace Application.Interface;

public interface IDiscordService
{
    /// <summary>私訊特定玩家（leader-led 組隊通知）。只能私訊同公會成員；對方關 DM/退公會會拋例外，由呼叫端分流。</summary>
    Task SendDirectMessageAsync(ulong discordId, string message);

    /// <summary>
    /// 同 <see cref="SendDirectMessageAsync(ulong,string)"/>，但附一列可點按鈕（discord-inline-actions）。
    /// 按鈕點擊由 bot 的互動 handler 依 <see cref="DmButton.CustomId"/> 處理。
    /// 回傳送出的 message id（dm-revoke-cleanup：邀請 DM 之後可能被自動撤銷 → 需存 id 供編輯）。
    /// </summary>
    Task<ulong> SendDirectMessageAsync(ulong discordId, string message, IReadOnlyList<DmButton> buttons);

    /// <summary>
    /// 送一則 embed DM + 一列按鈕（bot-composed-embeds）：如邀請 DM 用 embed 列出目前成員職業/攻擊力 + 接受/拒絕。
    /// 回傳送出的 message id（同上，供撤邀時編輯）。
    /// </summary>
    Task<ulong> SendDirectMessageAsync(ulong discordId, DmEmbed embed, IReadOnlyList<DmButton> buttons);

    /// <summary>
    /// 編輯先前送出的 DM（dm-revoke-cleanup）：把被自動撤銷的邀請 DM 改成 <paramref name="content"/>、移除按鈕、保留原 embed。
    /// 訊息已被對方刪除（NotFound）→ 由呼叫端 handler 吞掉。
    /// </summary>
    Task EditDirectMessageAsync(ulong discordId, ulong messageId, string content);
}
