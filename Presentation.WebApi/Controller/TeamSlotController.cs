using Application.DTOs;
using Application.Interface;
using Microsoft.AspNetCore.Mvc;
using Presentation.WebApi.Extensions;

namespace Presentation.WebApi.Controller;

[ApiController]
[Route("api/[controller]")]
public class TeamSlotController : ControllerBase
{
    private readonly ITeamLeaderService _teamLeaderService;

    public TeamSlotController(ITeamLeaderService teamLeaderService)
    {
        _teamLeaderService = teamLeaderService;
    }

    // 隊長開隊 + 條件（leader-led，§5「不分權」→ 任何登入者可開隊）。LeaderDiscordId 用登入身分、不信任 client。
    [HttpPost]
    public async Task<IActionResult> CreateTeamAsync([FromBody] CreateTeamCommand command)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        command.LeaderDiscordId = discordId;
        var teamSlotId = await _teamLeaderService.CreateTeamAsync(command);
        return Ok(new { teamSlotId });
    }

    // Push：本期尚有空位的 leader 開放隊（玩家發現要申請哪隊）。
    [HttpGet("Open")]
    public async Task<IActionResult> GetOpenAsync()
    {
        if (!this.TryGetCurrentDiscordId(out _))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _teamLeaderService.GetOpenTeamsAsync());
    }

    // Push：某隊的申請佇列（隊長審核；服務內驗隊長擁有）。
    [HttpGet("{id:int}/Applications")]
    public async Task<IActionResult> GetApplicationsAsync(int id)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _teamLeaderService.GetApplicationsAsync(id, discordId));
    }

    // 玩家收到的待處理邀請（Pull 玩家端）。
    [HttpGet("/api/Me/Invitations")]
    public async Task<IActionResult> GetMyInvitationsAsync()
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _teamLeaderService.GetMyInvitationsAsync(discordId));
    }

    // 玩家已入隊的隊（跨隊行程）。
    [HttpGet("/api/Me/Teams")]
    public async Task<IActionResult> GetMyTeamsAsync()
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _teamLeaderService.GetMyTeamsAsync(discordId));
    }

    // 隊長「我開的隊」清單（hub 入口；含 confirmed/applied/invited 計數）。
    [HttpGet("/api/Me/LedTeams")]
    public async Task<IActionResult> GetMyLedTeamsAsync()
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _teamLeaderService.GetLedTeamsAsync(discordId));
    }

    // Pull：某隊符合條件的候選清單（回能力欄、不含 discord 身分，§9.12）。
    [HttpGet("{id:int}/Candidates")]
    public async Task<IActionResult> GetCandidatesAsync(int id)
    {
        if (!this.TryGetCurrentDiscordId(out _))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _teamLeaderService.GetCandidatesAsync(id));
    }

    // Pull：隊長邀請候選（→Invited）。只有隊長本人可邀。
    [HttpPost("{id:int}/Invitations")]
    public async Task<IActionResult> InviteAsync(int id, [FromBody] InviteMemberRequest request)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        await _teamLeaderService.InviteMemberAsync(id, request.CharacterId, discordId);
        return Ok();
    }

    // Pull：玩家回應自己收到的邀請 accept（→Confirmed）/ decline（→Rejected）。
    [HttpPut("{id:int}/Invitations/{memberId:int}")]
    public async Task<IActionResult> RespondInvitationAsync(int id, int memberId, [FromBody] InvitationActionRequest request)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        switch (request.Action)
        {
            case "accept":
                await _teamLeaderService.AcceptInviteAsync(memberId, discordId);
                break;
            case "decline":
                await _teamLeaderService.DeclineInviteAsync(memberId, discordId);
                break;
            default:
                return BadRequest(new { error = "InvalidAction" });
        }
        return Ok();
    }

    // Push：玩家申請入隊（→Applied，用本人角色）。
    [HttpPost("{id:int}/Applications")]
    public async Task<IActionResult> ApplyAsync(int id, [FromBody] ApplyRequest request)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        await _teamLeaderService.ApplyAsync(id, request.CharacterId, discordId);
        return Ok();
    }

    // 玩家自助退隊（Confirmed→Left，釋放位子）。只能退自己在該隊的成員資格（服務內以登入身分定位）。
    [HttpPost("{id:int}/Leave")]
    public async Task<IActionResult> LeaveTeamAsync(int id)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        await _teamLeaderService.LeaveTeamAsync(id, discordId);
        return Ok();
    }

    // 隊長轉讓——某隊 Confirmed 名冊（挑轉讓對象；服務內驗隊長擁有）。
    [HttpGet("{id:int}/Roster")]
    public async Task<IActionResult> GetTeamRosterAsync(int id)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _teamLeaderService.GetTeamRosterAsync(id, discordId));
    }

    // 隊長轉讓——提議轉給某成員（→PendingLeaderDiscordId，需對方接受）。
    [HttpPost("{id:int}/TransferLeader")]
    public async Task<IActionResult> ProposeTransferAsync(int id, [FromBody] TransferLeaderRequest request)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        await _teamLeaderService.ProposeLeaderTransferAsync(id, request.MemberId, discordId);
        return Ok();
    }

    // 隊長轉讓——被指定者回應 accept（→成為新隊長）/ decline。
    [HttpPut("{id:int}/TransferLeader")]
    public async Task<IActionResult> RespondTransferAsync(int id, [FromBody] TransferLeaderActionRequest request)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        await _teamLeaderService.RespondLeaderTransferAsync(id, discordId, request.Action);
        return Ok();
    }

    // 我收到的待處理隊長轉讓（收件匣）。
    [HttpGet("/api/Me/LeaderTransfers")]
    public async Task<IActionResult> GetMyLeaderTransfersAsync()
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _teamLeaderService.GetMyLeaderTransfersAsync(discordId));
    }

    // Push：隊長審核申請 approve（→Confirmed）/ reject（→Rejected）。
    [HttpPut("{id:int}/Applications/{memberId:int}")]
    public async Task<IActionResult> RespondApplicationAsync(int id, int memberId, [FromBody] ApplicationActionRequest request)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        switch (request.Action)
        {
            case "approve":
                await _teamLeaderService.ApproveAsync(memberId, discordId);
                break;
            case "reject":
                await _teamLeaderService.RejectAsync(memberId, discordId);
                break;
            default:
                return BadRequest(new { error = "InvalidAction" });
        }
        return Ok();
    }
}
