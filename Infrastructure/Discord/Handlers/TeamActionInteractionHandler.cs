using Application.Exceptions;
using Application.Interface;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Infrastructure.Discord;
using Npgsql;

namespace Presentation.Infrastructure.Discord.Handlers;

/// <summary>
/// DM 內建動作按鈕的互動處理（discord-inline-actions）：邀請（接受/拒絕）、申請審核（核准/拒絕）、
/// 轉讓回應（接受/拒絕）三族共用一套。點擊 → 走與網頁相同的 service 方法 → 編輯原 DM 成結果、移除按鈕。
/// 授權＝互動帶的點擊者 user id（免 JWT），本人/隊長檢查由各 service 方法內既有的 Forbidden 把關。
///
/// 生命週期：DSharpPlus v5 每事件自動開 scope（見 bot-di-scoping）→ 直接注入 scoped
/// <see cref="IUnitOfWork"/>+<see cref="ITeamLeaderService"/>；本 handler 自管交易（bot 無 UnitOfWorkMiddleware）。
/// 併發/冪等沿用 service 既有防護（advisory lock + 容量重讀 + xmin + uq_tsc_confirmed_overlap）。
/// </summary>
public class TeamActionInteractionHandler : IEventHandler<ComponentInteractionCreatedEventArgs>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITeamLeaderService _teamLeaderService;
    private readonly IProfileService _profileService;

    public TeamActionInteractionHandler(IUnitOfWork unitOfWork, ITeamLeaderService teamLeaderService, IProfileService profileService)
    {
        _unitOfWork = unitOfWork;
        _teamLeaderService = teamLeaderService;
        _profileService = profileService;
    }

    public async Task HandleEventAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs eventArgs)
    {
        // 只處理團隊動作按鈕；其餘 component 互動略過。
        if (!TeamActionButton.TryParse(eventArgs.Id, out var family, out var positive, out var id))
            return;

        // 3 秒回應窗：先 DeferredMessageUpdate ack（不改訊息），DB 處理完再 edit。
        await eventArgs.Interaction.CreateResponseAsync(
            DiscordInteractionResponseType.DeferredMessageUpdate, new DiscordInteractionResponseBuilder());

        var result = await HandleAsync(family, positive, id, eventArgs.User.Id);

        // 編輯原 DM：加結果文字、移除按鈕（不帶 components），但**保留原本的 embed**（隊伍資訊/打王時間）→
        // 玩家接受後仍能回頭看王與時段（bot-composed-embeds）。純文字通知無 embed → 只顯示結果。
        var edit = new DiscordWebhookBuilder().WithContent(result);
        foreach (var embed in eventArgs.Message.Embeds)
            edit.AddEmbed(embed);
        await eventArgs.Interaction.EditOriginalResponseAsync(edit);
    }

    /// <summary>
    /// 純 seam（不碰 DSharpPlus，可單元測試）：開交易 → 依族別/正負向分派 service 方法 → commit；
    /// 例外分流成使用者可讀訊息（rollback）。非預期錯誤 rethrow。
    /// </summary>
    public async Task<string> HandleAsync(TeamActionFamily family, bool positive, int id, ulong clickerId)
    {
        await _unitOfWork.BeginAsync();
        try
        {
            var result = await DispatchAsync(family, positive, id, clickerId);
            await _unitOfWork.CommitAsync();
            return result;
        }
        catch (ForbiddenException)
        {
            await _unitOfWork.RollbackAsync();
            return "這個動作不是給你的。";
        }
        catch (NotFoundException)
        {
            await _unitOfWork.RollbackAsync();
            return "找不到對應項目（可能已被處理）。";
        }
        catch (BusinessException ex)
        {
            await _unitOfWork.RollbackAsync();
            return $"無法處理：{ex.Message}";
        }
        catch (PostgresException pg) when (pg.SqlState == "23505")
        {
            // 跨隊同時段重疊（uq_tsc_confirmed_overlap）：WebApi 靠 middleware 轉 409，bot 自己接。
            await _unitOfWork.RollbackAsync();
            return "時段衝突：你在這個時段已有其他隊伍，無法加入。";
        }
        catch
        {
            await _unitOfWork.RollbackAsync();
            throw;
        }
    }

    private async Task<string> DispatchAsync(TeamActionFamily family, bool positive, int id, ulong clickerId)
    {
        switch (family)
        {
            case TeamActionFamily.Invite:
                if (positive)
                {
                    await _teamLeaderService.AcceptInviteAsync(id, clickerId);
                    return "✅ 已接受邀請、加入隊伍。";
                }
                await _teamLeaderService.DeclineInviteAsync(id, clickerId);
                return "已拒絕邀請。";

            case TeamActionFamily.Application:
                if (positive)
                {
                    await _teamLeaderService.ApproveAsync(id, clickerId);
                    return "✅ 已核准，該玩家入隊。";
                }
                await _teamLeaderService.RejectAsync(id, clickerId);
                return "已拒絕此申請。";

            case TeamActionFamily.Transfer:
                await _teamLeaderService.RespondLeaderTransferAsync(id, clickerId, positive ? "accept" : "decline");
                return positive ? "✅ 已接受轉讓、你成為新隊長。" : "已拒絕轉讓。";

            case TeamActionFamily.Freshness:
                // 對象＝點擊者本人（id 不帶意義）。留任→重置新鮮度；移除我→關參戰（保留時段）。
                if (positive)
                {
                    await _profileService.ReaffirmFreshnessAsync(clickerId);
                    return "✅ 已留在找團名單。";
                }
                await _profileService.OptOutSeekingAsync(clickerId);
                return "已移出找團名單（你填的可用時段仍保留，隨時可重新開啟）。";

            default:
                throw new BusinessException("未知的動作。");
        }
    }
}
