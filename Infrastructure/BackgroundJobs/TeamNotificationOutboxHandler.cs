using System.Text.Json;
using Application.Events;
using Application.Interface;
using Dapper;
using DSharpPlus.Exceptions;
using Infrastructure.Discord;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// 處理 <see cref="OutboxEventType.TeamNotification"/>：對指定玩家發 Discord DM（leader-led §11）。
/// 只註冊在 bot 行程（DiscordClient 在那）——正是 outbox 要跨的行程界線（寫在 API、送在 bot）。
/// 非嚴格冪等：Discord DM 無 idempotency key、送出是外部非交易副作用，無法真去重。
/// 重送（crash 於送出與批次 commit 之間／送出後斷線）頂多多發相同 DM——dispatcher 整批一交易，
/// 最多重送該批（BatchSize）筆；可接受，因站內「我的邀請/我的隊」清單才是權威（§11），不漏資料。
/// </summary>
public class TeamNotificationOutboxHandler : IOutboxHandler
{
    private readonly IDiscordService _discordService;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<TeamNotificationOutboxHandler> _logger;

    public TeamNotificationOutboxHandler(IDiscordService discordService, IDbConnectionFactory connectionFactory, ILogger<TeamNotificationOutboxHandler> logger)
    {
        _discordService = discordService;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public string Type => OutboxEventType.TeamNotification;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var e = JsonSerializer.Deserialize<TeamNotificationEvent>(payload)
                ?? throw new InvalidOperationException("TeamNotification payload 解析失敗");

        try
        {
            // 撤邀清理（dm-revoke-cleanup）：編輯被邀者原 DM 成「已失效」+ 移按鈕，不送新訊息。
            // EditMessageId null（DM 未送出/id 未回寫）→ 跳過（死按鈕退化、可接受，見計畫已知邊界）。
            if (e.Action == TeamNotificationAction.InviteRevokedCleanup)
            {
                if (e.EditMessageId is { } editId)
                    await _discordService.EditDirectMessageAsync(e.TargetDiscordId, editId, e.Message);
                return;
            }

            // 可動作通知（邀請/申請審核/轉讓）→ 附對應按鈕（discord-inline-actions）。
            // 帶 Embed（目前：邀請 roster）→ 用 embed 呈現（bot-composed-embeds）；否則純文字 fallback。
            // None（含舊事件反序列化）→ 純文字。
            if (e.Action != TeamNotificationAction.None && e.ActionId is { } actionId)
            {
                var buttons = BuildButtons(e.Action, actionId);
                var messageId = e.Embed is { } embedData
                    ? await _discordService.SendDirectMessageAsync(e.TargetDiscordId, BuildActionEmbed(e.Action, embedData), buttons)
                    : await _discordService.SendDirectMessageAsync(e.TargetDiscordId, e.Message, buttons);

                // 邀請 DM 之後可能被自動撤銷 → 回寫 message id 供撤銷時編輯（dm-revoke-cleanup）。
                // 只邀請需要（申請審核/轉讓 DM 不會被自動撤銷）。
                if (e.Action == TeamNotificationAction.InviteResponse)
                    await PersistDmMessageIdAsync(actionId, messageId);
            }
            else
            {
                await _discordService.SendDirectMessageAsync(e.TargetDiscordId, e.Message);
            }
        }
        catch (UnauthorizedException)
        {
            // 對方關閉「接收伺服器成員 DM」→ 永久失敗：不 rethrow，讓 outbox 標 processed、不重試（避免毒訊息）。
            // 站內「我的邀請/我的隊」清單仍是權威真相（§11），玩家登入照樣看得到，不漏。
            _logger.LogInformation("玩家 {Id} 關閉 DM，通知略過", e.TargetDiscordId);
        }
        catch (NotFoundException)
        {
            // 已退公會（bot 只能私訊同公會者）→ 永久失敗，同樣吞掉不重試。
            _logger.LogInformation("玩家 {Id} 不在公會，通知略過", e.TargetDiscordId);
        }
        // 其餘例外（網路、429 限流等暫時失敗）→ 讓它 throw → outbox 重試（暫時錯才該重試）。
    }

    // 回寫邀請 DM 的 message id 到成員列（供撤邀時編輯 DM）。自開專屬連線（factory，singleton-safe，同 poller 慣例）。
    // best-effort：寫失敗只記 log、不 rethrow——否則整筆 outbox 會重送、重發 DM（重送比丟失 id 糟）。
    private async Task PersistDmMessageIdAsync(int memberId, ulong messageId)
    {
        try
        {
            await using var conn = _connectionFactory.Create();
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                """UPDATE "TeamSlotCharacter" SET "DmMessageId" = @mid WHERE "Id" = @id""",
                new { mid = (long)messageId, id = memberId });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "回寫 DmMessageId 失敗 memberId={Id}（撤邀時將無法編輯該 DM）", memberId);
        }
    }

    // 依動作組「正向/負向」兩顆按鈕（label 依族別：邀請/轉讓＝接受，申請＝核准）。
    private static IReadOnlyList<DmButton> BuildButtons(TeamNotificationAction action, int id)
    {
        var (family, positiveLabel, negativeLabel) = action switch
        {
            TeamNotificationAction.InviteResponse => (TeamActionFamily.Invite, "接受", "拒絕"),
            TeamNotificationAction.ApplicationReview => (TeamActionFamily.Application, "核准", "拒絕"),
            TeamNotificationAction.TransferResponse => (TeamActionFamily.Transfer, "接受", "拒絕"),
            _ => throw new InvalidOperationException($"未支援的通知動作 {action}")
        };
        return new[]
        {
            new DmButton(TeamActionButton.CustomId(family, true, id), positiveLabel, DmButtonStyle.Success),
            new DmButton(TeamActionButton.CustomId(family, false, id), negativeLabel, DmButtonStyle.Danger)
        };
    }

    // 邀請/申請 → embed（bot-composed-embeds）：標題＝王名＋時間；內文「被邀/申請角色」+ 目前成員；頁尾缺額。
    // public static 供單元測試（事件→embed 映射）。
    public static DmEmbed BuildActionEmbed(TeamNotificationAction action, TeamEmbedData d)
    {
        var roster = d.Roster.Count == 0
            ? "目前隊伍尚無其他成員。"
            : "目前成員（職業　等級　攻擊力　祝福等級）：\n" +
              string.Join("\n", d.Roster.Select(r => $"{r.Job}　Lv{r.Level}　攻{r.AttackPower}　祝福{r.MapleBlessingLevel}"));
        // 轉讓無「主角角色」→ 放轉讓說明一行（否則看不出是轉讓）；邀請/申請則放被邀/申請角色一行。
        var description = action switch
        {
            TeamNotificationAction.TransferResponse => $"轉讓隊長\n\n{roster}",
            _ => $"{(action == TeamNotificationAction.ApplicationReview ? "申請角色" : "被邀角色")}：" +
                 $"{d.SubjectName}　{d.SubjectJob}　Lv{d.SubjectLevel}　攻{d.SubjectAttackPower}　祝福{d.SubjectMapleBlessingLevel}\n\n{roster}"
        };
        var vacancy = Math.Max(0, d.Capacity - d.Roster.Count);
        return new DmEmbed($"{d.BossName}　{d.TimeText}", description, $"缺額 {vacancy}／{d.Capacity}");
    }
}
