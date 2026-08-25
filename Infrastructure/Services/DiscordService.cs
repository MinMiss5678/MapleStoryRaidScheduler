using System.Collections.Concurrent;
using Application.Interface;
using Application.Options;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class DiscordService : IDiscordService
{
    private readonly DiscordClient _discordClient;
    private readonly DiscordOptions _discordOptions;

    // userId → DM 頻道快取（DM 頻道 id 對同一人永久固定）。命中則直接送訊，跳過「取成員 + 開 DM 頻道」兩次 REST。
    // per-pod 記憶體快取（KISS）：重啟即失、多 pod 各建各的、皆可接受；跨 pod 共用非必要（見 plans/2026-08-07-dm-notification-api-call-reduction.md）。
    private readonly ConcurrentDictionary<ulong, DiscordDmChannel> _dmChannelCache = new();

    public DiscordService(DiscordClient discordClient, IOptions<DiscordOptions> discordOptions)
    {
        _discordClient = discordClient;
        _discordOptions = discordOptions.Value;
    }

    public Task SendDirectMessageAsync(ulong discordId, string message)
        => SendAsync(discordId, dm => dm.SendMessageAsync(message));

    public Task SendDirectMessageAsync(ulong discordId, string message, IReadOnlyList<DmButton> buttons)
    {
        var builder = new DiscordMessageBuilder()
            .WithContent(message)
            .AddActionRowComponent(buttons.Select(ToComponent));
        return SendAsync(discordId, dm => dm.SendMessageAsync(builder));
    }

    public Task SendDirectMessageAsync(ulong discordId, DmEmbed embed, IReadOnlyList<DmButton> buttons)
    {
        var embedBuilder = new DiscordEmbedBuilder()
            .WithTitle(embed.Title)
            .WithDescription(embed.Description)
            .WithColor(new DiscordColor("#5865F2"));  // Discord blurple
        if (embed.Footer is { } footer)
            embedBuilder.WithFooter(footer, null);
        var builder = new DiscordMessageBuilder()
            .AddEmbed(embedBuilder.Build())
            .AddActionRowComponent(buttons.Select(ToComponent));
        return SendAsync(discordId, dm => dm.SendMessageAsync(builder));
    }

    /// <summary>
    /// 送訊共用路徑（快取 + 失效重建）：快取命中 → 熱路徑只打 1 次 REST（送訊本體）。DM 頻道理論上永不失效；
    /// 萬一 404（頻道失效）→ 清快取、退回完整路徑重建一次。
    /// UnauthorizedException(403，對方關 DM)不在此吞 → 往上由 handler 當永久失敗，維持既有語意。
    /// </summary>
    private async Task SendAsync(ulong discordId, Func<DiscordDmChannel, Task> send)
    {
        if (_dmChannelCache.TryGetValue(discordId, out var cached))
        {
            try
            {
                await send(cached);
                return;
            }
            catch (NotFoundException)
            {
                _dmChannelCache.TryRemove(discordId, out _);
                // 落到下方完整路徑重建 DM 頻道、重試一次
            }
        }

        var dm = await OpenDmChannelAsync(discordId);
        _dmChannelCache[discordId] = dm;
        await send(dm);
    }

    private static DiscordButtonComponent ToComponent(DmButton b) =>
        new(b.Style switch
        {
            DmButtonStyle.Primary => DiscordButtonStyle.Primary,
            DmButtonStyle.Secondary => DiscordButtonStyle.Secondary,
            DmButtonStyle.Success => DiscordButtonStyle.Success,
            DmButtonStyle.Danger => DiscordButtonStyle.Danger,
            _ => DiscordButtonStyle.Secondary
        }, b.CustomId, b.Label);

    /// <summary>
    /// 開 DM 頻道。成員先查本地快取（bot 已開 GuildMembers intent + 常駐 gateway → 多半命中、不打 REST），
    /// 未命中才 <c>GetMemberAsync</c> REST fallback（正確性不變）。<c>GetGuildAsync</c> 由 gateway 快取供應、不打 REST。
    /// 對方關 DM → UnauthorizedException(403)、退公會 → NotFoundException(404)：皆由呼叫端 handler 當永久失敗分流，不在此吞。
    /// </summary>
    private async Task<DiscordDmChannel> OpenDmChannelAsync(ulong discordId)
    {
        var guild = await _discordClient.GetGuildAsync(Convert.ToUInt64(_discordOptions.GuildId));
        var member = guild.Members.TryGetValue(discordId, out var cachedMember)
            ? cachedMember
            : await guild.GetMemberAsync(discordId);
        return await member.CreateDmChannelAsync();
    }
}
