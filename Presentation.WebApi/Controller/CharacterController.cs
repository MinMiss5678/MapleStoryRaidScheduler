using Application.DTOs;
using Application.Interface;
using Microsoft.AspNetCore.Mvc;
using Presentation.WebApi.Extensions;

namespace Presentation.WebApi.Controller;

[ApiController]
[Route("api/[controller]")]
public class CharacterController : ControllerBase
{
    private readonly ICharacterService _characterService;

    public CharacterController(ICharacterService characterService)
    {
        _characterService = characterService;
    }

    [HttpGet("GetWithDiscordName")]
    public async Task<IActionResult> GetWithDiscordNameAsync([FromQuery] int? bossId)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _characterService.GetWithDiscordNameAsync(discordId, bossId));
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CharacterRequest request)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        request.DiscordId = discordId;
        await _characterService.CreateAsync(request);

        return Ok(request);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAsync(string id, [FromBody] CharacterRequest request)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        request.Id = id;
        request.DiscordId = discordId;
        await _characterService.UpdateAsync(request);
        return Ok(request);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAsync(string id)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        await _characterService.DeleteAsync(discordId, id);
        return Ok();
    }
}