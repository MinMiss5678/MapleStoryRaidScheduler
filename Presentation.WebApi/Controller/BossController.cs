using Application.DTOs;
using Application.Interface;
using Microsoft.AspNetCore.Mvc;
using Presentation.WebApi.Attributes;

namespace Presentation.WebApi.Controller;

[ApiController]
[Route("api/[controller]")]
public class BossController : ControllerBase
{
    private readonly IBossService _bossService;

    public BossController(IBossService bossService)
    {
        _bossService = bossService;
    }

    [HttpGet("GetAll")]
    public async Task<IActionResult> GetAllAsync()
    {
        return Ok(await _bossService.GetAllAsync());
    }

    [AuthorizeRole("admin")]
    [HttpPost]
    public async Task<IActionResult> CreateBossAsync([FromBody] BossRequest request)
    {
        var id = await _bossService.CreateBossAsync(request);
        return Ok(id);
    }

    [AuthorizeRole("admin")]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBossAsync(int id, [FromBody] BossRequest request)
    {
        await _bossService.UpdateBossAsync(id, request);
        return Ok();
    }

    [AuthorizeRole("admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBossAsync(int id)
    {
        await _bossService.DeleteBossAsync(id);
        return Ok();
    }
}
