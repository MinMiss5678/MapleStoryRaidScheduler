using Application.DTOs;
using Application.Interface;
using Microsoft.AspNetCore.Mvc;
using Presentation.WebApi.Attributes;

namespace Presentation.WebApi.Controller;

[ApiController]
[Route("api/[controller]")]
public class ScheduleController : ControllerBase
{
    private readonly IScheduleService _scheduleService;

    public ScheduleController(IScheduleService scheduleService)
    {
        _scheduleService = scheduleService;
    }
    
    [AuthorizeRole]
    [HttpPost("AutoSchedule")]
    public async Task<IActionResult> AutoScheduleAsync([FromBody] AutoScheduleRequest autoScheduleRequest)
    {
        return Ok(await _scheduleService.AutoScheduleAsync(autoScheduleRequest.BossId, autoScheduleRequest.MinMembers));
    }
}