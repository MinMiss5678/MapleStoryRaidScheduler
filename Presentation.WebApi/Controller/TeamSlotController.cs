using Application.DTOs;
using Application.Interface;
using Microsoft.AspNetCore.Mvc;
using Presentation.WebApi.Attributes;

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
        var discordId = User.Claims.FirstOrDefault(c => c.Type == "discordId")?.Value;
        if (discordId == null)
        {
            return Unauthorized(new { error = "NotAuthenticated" });
        }

        return Ok(await _teamSlotService.GetByDiscordIdAsync(Convert.ToUInt64(discordId)));
    }
    
    [HttpPut]
    public async Task<IActionResult> UpdateAsync([FromBody] TeamSlotUpdateRequest teamSlotUpdateRequest)
    {
        var isAdmin = User.IsInRole("Admin");
        var discordId = Convert.ToUInt64(User.Claims.FirstOrDefault(c => c.Type == "discordId")?.Value);
        await _teamSlotService.UpdateAsync(teamSlotUpdateRequest, isAdmin, discordId);
        var teamSlots = await _teamSlotService.GetByBossIdAsync(teamSlotUpdateRequest.BossId);
        
        return Ok(teamSlots);
    }
}