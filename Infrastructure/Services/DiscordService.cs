using System.Collections.Concurrent;
using Application.Interface;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;

namespace Infrastructure.Services;

public class DiscordService : IDiscordService
{
    private readonly DiscordClient _discordClient;

    // userId → DM 頻道快取（DM 頻道 id 對同一人永久固定）。命中則直接送訊，跳過「取 user + 開 DM 頻道」兩次 REST。
    // per-pod 記憶體快取（KISS）：重啟即失、多 pod 各建各的、皆可接受；跨 pod 共用非必要（見 plans/2026-08-07-dm-notification-api-call-reduction.md）。
    private readonly ConcurrentDictionary<ulong, DiscordDmChannel> _dmChannelCache = new();

    public DiscordService(DiscordClient discordClient)
    {
        _discordClient = discordClient;
    }

    public Task SendDirectMessageAsync(ulong discordId, string message)
        => SendAsync(discordId, dm => dm.SendMessageAsync(message));

    public async Task<ulong> SendDirectMessageAsync(ulong discordId, string message, IReadOnlyList<DmButton> buttons)
    {
        var builder = new DiscordMessageBuilder()
            .WithContent(message)
            .AddActionRowComponent(buttons.Select(ToComponent));
        var msg = await SendAsync(discordId, dm => dm.SendMessageAsync(builder));
        return msg.Id;
    }

    public async Task<ulong> SendDirectMessageAsync(ulong discordId, DmEmbed embed, IReadOnlyList<DmButton> buttons)
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
        var msg = await SendAsync(discordId, dm => dm.SendMessageAsync(builder));
        return msg.Id;
    }

    /// <summary>編輯先前的 DM（dm-revoke-cleanup）：改內容、清按鈕、保留原 embed；訊息已被刪（NotFound）→ 吞掉。</summary>
    public async Task EditDirectMessageAsync(ulong discordId, ulong messageId, string content)
    {
        try
        {
            await SendAsync(discordId, async dm =>
            {
                var msg = await dm.GetMessageAsync(messageId);
                var builder = new DiscordMessageBuilder().WithContent(content);
                foreach (var em in msg.Embeds)
                    builder.AddEmbed(em);
                return await msg.ModifyAsync(builder);   // 不帶 components → 移除按鈕；保留 embed
            });
        }
        catch (NotFoundException)
        {
            // 訊息已被對方刪除（或頻道失效重試後仍 404）→ 無可編輯，忽略。
        }
    }

    /// <summary>
    /// 送訊共用路徑（快取 + 失效重建）：快取命中 → 熱路徑只打 1 次 REST（送訊本體）。DM 頻道理論上永不失效；
    /// 萬一 404（頻道失效）→ 清快取、退回完整路徑重建一次。
    /// UnauthorizedException(403，對方關 DM)不在此吞 → 往上由 handler 當永久失敗，維持既有語意。
    /// </summary>
    private async Task<T> SendAsync<T>(ulong discordId, Func<DiscordDmChannel, Task<T>> action)
    {
        if (_dmChannelCache.TryGetValue(discordId, out var cached))
        {
            try
            {
                return await action(cached);
            }
            catch (NotFoundException)
            {
                _dmChannelCache.TryRemove(discordId, out _);
                // 落到下方完整路徑重建 DM 頻道、重試一次
            }
        }

        var dm = await OpenDmChannelAsync(discordId);
        _dmChannelCache[discordId] = dm;
        return await action(dm);
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
    /// 開 DM 頻道：<c>GetUserAsync</c>(REST、cache-aware) → <c>CreateDmChannelAsync</c>。
    /// 不經 guild 成員快取 → gateway 未連線（dispatcher 角色，見 plans/2026-09-04-multi-pod-outbox-dispatch.md）也可用，
    /// 且避開「REST 取得的 guild `_members` 為 null → GetMemberAsync NRE」。有 gateway 時 GetUserAsync 命中 user 快取、不打 REST；
    /// 熱路徑仍靠上方 _dmChannelCache。對方關 DM → UnauthorizedException(403)、查無此人 → NotFoundException(404)：
    /// 皆由呼叫端 handler 當永久失敗分流，不在此吞。
    /// </summary>
    private async Task<DiscordDmChannel> OpenDmChannelAsync(ulong discordId)
    {
        var user = await _discordClient.GetUserAsync(discordId);
        return await user.CreateDmChannelAsync();
    }
}
