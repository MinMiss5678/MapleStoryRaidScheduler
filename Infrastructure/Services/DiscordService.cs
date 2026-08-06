using Application.Interface;
using Application.Options;
using DSharpPlus;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class DiscordService : IDiscordService
{
    private readonly DiscordClient _discordClient;
    private readonly DiscordOptions _discordOptions;

    public DiscordService(DiscordClient discordClient, IOptions<DiscordOptions> discordOptions)
    {
        _discordClient = discordClient;
        _discordOptions = discordOptions.Value;
    }

    public async Task SendMessageAsync(string message)
    {
        var channel = await _discordClient.GetChannelAsync(Convert.ToUInt64(_discordOptions.ChannelId));
        await channel.SendMessageAsync(message);
    }

    public async Task SendDirectMessageAsync(ulong discordId, string message)
    {
        // DSharpPlus 沒有直接對 userId 送的頂層方法：取公會成員 → member.SendMessageAsync 自動建/用 DM channel。
        // GuildMembers intent 已開（Presentation/Program.cs）故抓得到成員。對方關 DM → UnauthorizedException(403)、
        // 退公會 → NotFoundException(404)：皆由呼叫端（handler）當永久失敗分流，不在此吞。
        var guild = await _discordClient.GetGuildAsync(Convert.ToUInt64(_discordOptions.GuildId));
        var member = await guild.GetMemberAsync(discordId);
        await member.SendMessageAsync(message);
    }
}
