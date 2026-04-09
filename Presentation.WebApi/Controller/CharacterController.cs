using Application.Interface;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;

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
        var discordId = User.Claims.FirstOrDefault(c => c.Type == "discordId")?.Value;
        if (discordId == null)
        {
            return Unauthorized(new { error = "NotAuthenticated" });
        }

        var characters = await _characterService.GetWithDiscordNameAsync(Convert.ToUInt64(discordId), bossId);

        return Ok(characters);
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] Character character)
    {
        var discordId = User.Claims.FirstOrDefault(c => c.Type == "discordId")?.Value;
        if (discordId == null)
        {
            return Unauthorized(new { error = "NotAuthenticated" });
        }

        character.DiscordId = Convert.ToUInt64(discordId);
        
        await _characterService.CreateAsync(character);
        
        return Ok(character);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAsync(string id, [FromBody] Character character)
    {
        character.Id = id;
        character.DiscordId = Convert.ToUInt64(User.Claims.FirstOrDefault(c => c.Type == "discordId")?.Value);
        
        var result = await _characterService.UpdateAsync(character);
        if (result == 1)
        {
            return Ok(character);
        }
        else
        {
            return StatusCode(500, new { message = "Failed to update character" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAsync(string id)
    {
        var discordId = Convert.ToUInt64(User.Claims.FirstOrDefault(c => c.Type == "discordId")?.Value);
        var result = await _characterService.DeleteAsync(discordId, id);
        if (result == 1)
        {
            return Ok();
        }
        else
        {
            return StatusCode(500, new { message = "Failed to delete character" });
        }
    }
}