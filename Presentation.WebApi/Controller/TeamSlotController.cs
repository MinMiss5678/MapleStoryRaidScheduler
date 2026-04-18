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

    public TeamSlotController(ITeamSlotService teamSlotService)
    {
        _teamSlotService = teamSlotService;
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
        await _teamSlotService.UpdateAsync(teamSlotUpdateRequest, isAdmin, discordId);
        var teamSlots = await _teamSlotService.GetByBossIdAsync(teamSlotUpdateRequest.BossId);

        return Ok(teamSlots);
    }
}