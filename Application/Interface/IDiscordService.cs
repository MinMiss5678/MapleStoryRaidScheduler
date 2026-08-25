namespace Application.Interface;

public interface IDiscordService
{
    /// <summary>私訊特定玩家（leader-led 組隊通知）。只能私訊同公會成員；對方關 DM/退公會會拋例外，由呼叫端分流。</summary>
    Task SendDirectMessageAsync(ulong discordId, string message);

    /// <summary>
    /// 同 <see cref="SendDirectMessageAsync(ulong,string)"/>，但附一列可點按鈕（discord-inline-actions）。
    /// 按鈕點擊由 bot 的互動 handler 依 <see cref="DmButton.CustomId"/> 處理。
    /// </summary>
    Task SendDirectMessageAsync(ulong discordId, string message, IReadOnlyList<DmButton> buttons);

    /// <summary>
    /// 送一則 embed DM + 一列按鈕（bot-composed-embeds）：如邀請 DM 用 embed 列出目前成員職業/攻擊力 + 接受/拒絕。
    /// </summary>
    Task SendDirectMessageAsync(ulong discordId, DmEmbed embed, IReadOnlyList<DmButton> buttons);
}
