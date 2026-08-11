using Application.DTOs;
using Application.Interface;
using Microsoft.AspNetCore.Mvc;
using Presentation.WebApi.Extensions;

namespace Presentation.WebApi.Controller;

/// <summary>玩家自助管理可用時段的日期 override（period-less §8 Phase 2b-write）。</summary>
[ApiController]
[Route("api/[controller]")]
public class AvailabilityOverrideController : ControllerBase
{
    private readonly IAvailabilityOverrideService _service;

    public AvailabilityOverrideController(IAvailabilityOverrideService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetMineAsync()
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _service.GetMineAsync(discordId));
    }

    [HttpPost]
    public async Task<IActionResult> AddAsync([FromBody] AvailabilityOverrideCreateCommand command)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        command.DiscordId = discordId;
        await _service.AddAsync(command);
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> RemoveAsync(int id)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        await _service.RemoveAsync(discordId, id);
        return Ok();
    }
}
