using Application.Interface;
using Microsoft.AspNetCore.Mvc;

namespace Presentation.WebApi.Controller;

[ApiController]
[Route("api/[controller]")]
public class PeriodController : ControllerBase
{
    private readonly IPeriodService _periodService;

    public PeriodController(IPeriodService periodService)
    {
        _periodService = periodService;
    }
    
    [HttpGet("GetByNow")]
    public async Task<IActionResult> GetByNowAsync()
    {
        return Ok(await _periodService.GetByNowAsync());
    }
}