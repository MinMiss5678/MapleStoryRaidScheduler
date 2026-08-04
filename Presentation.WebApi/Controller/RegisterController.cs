using Application.DTOs;
using Application.Interface;
using Microsoft.AspNetCore.Mvc;
using Presentation.WebApi.Extensions;

namespace Presentation.WebApi.Controller;

[ApiController]
[Route("api/[controller]")]
public class RegisterController : ControllerBase
{
    private readonly IRegisterService _registerService;
    private readonly IRegisterQueryService _registerQueryService;

    public RegisterController(IRegisterService registerService, IRegisterQueryService registerQueryService)
    {
        _registerService = registerService;
        _registerQueryService = registerQueryService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync()
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _registerQueryService.GetAsync(discordId));
    }

    [HttpGet("GetLast")]
    public async Task<IActionResult> GetLastAsync()
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        return Ok(await _registerQueryService.GetLastAsync(discordId));
    }

    [HttpGet("GetByQuery")]
    public async Task<IActionResult> GetByQueryAsync([FromQuery] RegisterGetByQueryRequest request)
    {
        return Ok(await _registerQueryService.GetByQueryAsync(request));
    }

    // 目前開放報名週期的截止時間（權威值，與後端擋報名同一套）。前端 banner 顯示用，不含使用者資料。
    [HttpGet("Deadline")]
    public async Task<IActionResult> GetDeadlineAsync()
    {
        return Ok(new { deadline = await _registerQueryService.GetCurrentDeadlineAsync() });
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] RegisterCreateCommand command)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        command.DiscordId = discordId;
        await _registerService.CreateAsync(command);

        return Ok();
    }

    [HttpPut]
    public async Task<IActionResult> UpdateAsync([FromBody] RegisterUpdateCommand command)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        command.DiscordId = discordId;
        await _registerService.UpdateAsync(command);

        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAsync(int id)
    {
        if (!this.TryGetCurrentDiscordId(out var discordId))
            return Unauthorized(new { error = "NotAuthenticated" });

        await _registerService.DeleteAsync(discordId, id);

        return Ok();
    }
}
