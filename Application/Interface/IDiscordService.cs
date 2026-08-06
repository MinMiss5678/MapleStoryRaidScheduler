namespace Application.Interface;

public interface IDiscordService
{
    Task SendMessageAsync(string message);

    /// <summary>私訊特定玩家（leader-led 組隊通知）。只能私訊同公會成員；對方關 DM/退公會會拋例外，由呼叫端分流。</summary>
    Task SendDirectMessageAsync(ulong discordId, string message);
}
