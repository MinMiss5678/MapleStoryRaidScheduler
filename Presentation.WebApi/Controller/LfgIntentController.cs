using Application.DTOs;
using Application.Interface;
using Application.Queries;
using Microsoft.AspNetCore.Mvc;
using Presentation.WebApi.Extensions;

namespace Presentation.WebApi.Controller;

/// <summary>即時找隊（period-less §8 Phase 3）：看板 + 發布/取消找隊意圖。</summary>
[ApiController]
[Route("api/[controller]")]
public class LfgIntentController : ControllerBase
{
    private readonly ILfgService _service;
    private readonly ILfgQuery _query;

    public LfgIntentController(ILfgService service, ILfgQuery query)
    {
        _service = service;
        _query = query;
    }

    [HttpGet]
    public async Task<IActionResult> GetBoardAsync()
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _query.GetBoardAsync(discordId));
    }

    [HttpPost]
    public async Task<IActionResult> PostAsync([FromBody] LfgIntentCreateCommand command)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        command.DiscordId = discordId;
        await _service.PostAsync(command);
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> CancelAsync(int id)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        await _service.CancelAsync(discordId, id);
        return Ok();
    }
}
