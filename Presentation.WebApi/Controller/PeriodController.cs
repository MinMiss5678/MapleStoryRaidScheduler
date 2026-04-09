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
        var result = await _periodService.GetByNowAsync();
        if (result == null) return NotFound();
        
        return Ok(result);
    }
}