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

    // per 角色 per 王 通關數：玩家自填（取代舊 register 退場後缺的輸入路徑）。只能讀寫自己的角色。
    [HttpGet("{id}/BossClears")]
    public async Task<IActionResult> GetBossClearsAsync(string id)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _characterService.GetBossClearsAsync(discordId, id));
    }

    [HttpPost("{id}/BossClears")]
    public async Task<IActionResult> SaveBossClearsAsync(string id, [FromBody] IEnumerable<BossClearDto> clears)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        await _characterService.SaveBossClearsAsync(discordId, id, clears);
        return Ok();
    }

    // per 角色 偏好王（複選）：候選匹配軟訊號來源。只能讀寫自己的角色；PUT = 整批取代。
    [HttpGet("{id}/PreferredBosses")]
    public async Task<IActionResult> GetPreferredBossesAsync(string id)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _characterService.GetPreferredBossesAsync(discordId, id));
    }

    [HttpPut("{id}/PreferredBosses")]
    public async Task<IActionResult> SavePreferredBossesAsync(string id, [FromBody] IEnumerable<int> bossIds)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        await _characterService.SavePreferredBossesAsync(discordId, id, bossIds);
        return Ok();
    }
}
