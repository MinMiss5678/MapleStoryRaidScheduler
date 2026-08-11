using Application.DTOs;
using Application.Interface;
using Microsoft.AspNetCore.Mvc;
using Presentation.WebApi.Extensions;

namespace Presentation.WebApi.Controller;

/// <summary>玩家 profile（period-less §8：報名 UX 大改）：常設可用時段 + 角色參戰 opt-in。</summary>
[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly IProfileService _service;

    public ProfileController(IProfileService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync()
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _service.GetAsync(discordId));
    }

    [HttpPut]
    public async Task<IActionResult> SaveAsync([FromBody] ProfileSaveCommand command)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        command.DiscordId = discordId;
        await _service.SaveAsync(command);
        return Ok();
    }
}
