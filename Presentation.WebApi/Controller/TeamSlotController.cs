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
