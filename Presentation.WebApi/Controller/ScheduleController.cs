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
    [HttpPost("AutoScheduleWithTemplate")]
    public async Task<IActionResult> AutoScheduleWithTemplateAsync([FromBody] AutoScheduleWithTemplateRequest request)
    {
        return Ok(await _scheduleService.AutoScheduleWithTemplateAsync(request.BossId, request.TemplateId));
    }
}

public class AutoScheduleWithTemplateRequest
{
    public int BossId { get; set; }
    public int TemplateId { get; set; }
}