using Application.DTOs;
using Application.Interface;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Presentation.WebApi.Controller;

[ApiController]
[Route("api/[controller]")]
public class RegisterController: ControllerBase
{
    private readonly IRegisterService _registerService;

    public RegisterController(IRegisterService registerService)
    {
        _registerService = registerService;
    }
    
    [HttpGet]
    public async Task<IActionResult> GetAsync()
    {
        var discordId = User.Claims.FirstOrDefault(c => c.Type == "discordId")?.Value;
        if (discordId == null)
        {
            return Unauthorized(new { error = "NotAuthenticated" });
        }
        
        var registers = await _registerService.GetAsync(Convert.ToUInt64(discordId));
        
        return Ok(registers);
    }

    [HttpGet("GetByQuery")]
    public async Task<IActionResult> GetByQueryAsync([FromQuery] RegisterGetByQueryRequest request)
    {
        return Ok(await _registerService.GetByQueryAsync(request));
    }
    
    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] Register register)
    {
        var discordId = User.Claims.FirstOrDefault(c => c.Type == "discordId")?.Value;
        if (discordId == null)
        {
            return Unauthorized(new { error = "NotAuthenticated" });
        }

        register.DiscordId = Convert.ToUInt64(discordId);
        await _registerService.CreateAsync(register);
        
        return Ok();
    }
    
    [HttpPut]
    public async Task<IActionResult> UpdateAsync([FromBody] Register register)
    {
        var discordId = User.Claims.FirstOrDefault(c => c.Type == "discordId")?.Value;
        if (discordId == null)
        {
            return Unauthorized(new { error = "NotAuthenticated" });
        }

        register.DiscordId = Convert.ToUInt64(discordId);
        await _registerService.UpdateAsync(register);
        
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAsync(int id)
    {
        var discordId = User.Claims.FirstOrDefault(c => c.Type == "discordId")?.Value;
        if (discordId == null)
        {
            return Unauthorized(new { error = "NotAuthenticated" });
        }
        else
        {
            await _registerService.DeleteAsync(Convert.ToUInt64(discordId), id);
            
            return Ok();
        }
    }
}