using Application.DTOs;
using Application.Interface;
using Microsoft.AspNetCore.Mvc;
using Presentation.WebApi.Attributes;
using Presentation.WebApi.Extensions;

namespace Presentation.WebApi.Controller;

[ApiController]
[Route("api/[controller]")]
public class TeamSlotController : ControllerBase
{
    private readonly ITeamSlotService _teamSlotService;
    private readonly ITeamLeaderService _teamLeaderService;

    public TeamSlotController(ITeamSlotService teamSlotService, ITeamLeaderService teamLeaderService)
    {
        _teamSlotService = teamSlotService;
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

    [HttpGet]
    public async Task<IActionResult> GetAsync([FromQuery] int bossId)
    {
        return Ok(await _teamSlotService.GetByBossIdAsync(bossId));
    }

    [HttpGet("GetByDiscordId")]
    public async Task<IActionResult> GetByDiscordIdAsync()
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _teamSlotService.GetByDiscordIdAsync(discordId));
    }

    [HttpPut]
    public async Task<IActionResult> UpdateAsync([FromBody] TeamSlotUpdateRequest teamSlotUpdateRequest)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        var isAdmin = User.IsInRole("admin");
        var result = await _teamSlotService.UpdateAsync(teamSlotUpdateRequest, isAdmin, discordId);
        var teamSlots = await _teamSlotService.GetByBossIdAsync(teamSlotUpdateRequest.BossId);

        return Ok(new { conflictedTeamSlotIds = result.ConflictedTeamSlotIds, teamSlots });
    }

    // 玩家補位：把自己的角色加進某個空位。跟 UpdateAsync 分開的獨立窄範圍端點，
    // payload 型別上放不進別人的資料（DiscordId 一律用登入身分），見
    // plans/2026-07-31-teamslot-fill-endpoint-separation.md。
    [HttpPost("Fill")]
    public async Task<IActionResult> FillAsync([FromBody] TeamSlotFillRequest request)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        // 回傳寫入後重新查詢的最新隊伍資料（含新角色真實 Id/Version），跟 UpdateAsync 同一套慣例：
        // 寫入 + 重查包進同一個回應，前端不用再自己拼湊本地樂觀更新的資料、也不用多打一次 API。
        var teamSlot = await _teamSlotService.FillSlotAsync(request, discordId);

        return Ok(teamSlot);
    }
}
