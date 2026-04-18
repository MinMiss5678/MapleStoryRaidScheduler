using Application.DTOs;
using Application.Interface;
using Microsoft.AspNetCore.Mvc;

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

    [HttpGet("{bossId}/Templates")]
    public async Task<IActionResult> GetTemplatesAsync(int bossId)
    {
        return Ok(await _bossService.GetTemplatesByBossIdAsync(bossId));
    }

    [HttpGet("Templates/{templateId}")]
    public async Task<IActionResult> GetTemplateByIdAsync(int templateId)
    {
        return Ok(await _bossService.GetTemplateByIdAsync(templateId));
    }

    [HttpPost("Templates")]
    public async Task<IActionResult> CreateTemplateAsync([FromBody] BossTemplateRequest request)
    {
        var id = await _bossService.CreateTemplateAsync(request);
        return Ok(id);
    }

    [HttpPut("Templates/{templateId}")]
    public async Task<IActionResult> UpdateTemplateAsync(int templateId, [FromBody] BossTemplateRequest request)
    {
        await _bossService.UpdateTemplateAsync(templateId, request);
        return Ok();
    }

    [HttpDelete("Templates/{templateId}")]
    public async Task<IActionResult> DeleteTemplateAsync(int templateId)
    {
        await _bossService.DeleteTemplateAsync(templateId);
        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> CreateBossAsync([FromBody] BossRequest request)
    {
        var id = await _bossService.CreateBossAsync(request);
        return Ok(id);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBossAsync(int id, [FromBody] BossRequest request)
    {
        await _bossService.UpdateBossAsync(id, request);
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBossAsync(int id)
    {
        await _bossService.DeleteBossAsync(id);
        return Ok();
    }
}