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

    [AuthorizeRole("admin")]
    [HttpPost("AutoScheduleWithTemplate")]
    public async Task<IActionResult> AutoScheduleWithTemplateAsync([FromBody] AutoScheduleWithTemplateRequest request)
    {
        return Ok(await _scheduleService.AutoScheduleWithTemplateAsync(request.BossId, request.TemplateId));
    }
}

public class AutoScheduleWithTemplateRequest
{
    // BossId/TemplateId 的存在性由 ScheduleService 驗（TemplateId 不存在回 404、BossId null-safe）
    public int BossId { get; set; }
    public int TemplateId { get; set; }
}
