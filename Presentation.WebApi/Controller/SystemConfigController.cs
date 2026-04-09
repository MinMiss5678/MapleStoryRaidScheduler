using Application.Interface;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Presentation.WebApi.Controller;

[ApiController]
[Route("api/[controller]")]
public class SystemConfigController : ControllerBase
{
    private readonly ISystemConfigService _systemConfigService;

    public SystemConfigController(ISystemConfigService systemConfigService)
    {
        _systemConfigService = systemConfigService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync()
    {
        var result = await _systemConfigService.GetAsync();
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateAsync([FromBody] SystemConfig config)
    {
        await _systemConfigService.UpdateAsync(config);
        return Ok();
    }
}
