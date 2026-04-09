using Application.Interface;
using Domain.Entities;
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
        var result = await _bossService.GetAllAsync();
        return Ok(result);
    }

    [HttpGet("{bossId}/Templates")]
    public async Task<IActionResult> GetTemplatesAsync(int bossId)
    {
        var result = await _bossService.GetTemplatesByBossIdAsync(bossId);
        return Ok(result);
    }

    [HttpGet("Templates/{templateId}")]
    public async Task<IActionResult> GetTemplateByIdAsync(int templateId)
    {
        var template = await _bossService.GetTemplateByIdAsync(templateId);
        if (template == null) return NotFound();
        return Ok(template);
    }

    [HttpPost("Templates")]
    public async Task<IActionResult> CreateTemplateAsync([FromBody] BossTemplate template)
    {
        var id = await _bossService.CreateTemplateAsync(template);
        return Ok(id);
    }

    [HttpPut("Templates/{templateId}")]
    public async Task<IActionResult> UpdateTemplateAsync(int templateId, [FromBody] BossTemplate template)
    {
        template.Id = templateId;
        var result = await _bossService.UpdateTemplateAsync(template);
        if (result) return Ok();
        return BadRequest();
    }

    [HttpDelete("Templates/{templateId}")]
    public async Task<IActionResult> DeleteTemplateAsync(int templateId)
    {
        var result = await _bossService.DeleteTemplateAsync(templateId);
        if (result) return Ok();
        return BadRequest();
    }

    [HttpPost]
    public async Task<IActionResult> CreateBossAsync([FromBody] Boss boss)
    {
        var id = await _bossService.CreateBossAsync(boss);
        return Ok(id);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBossAsync(int id, [FromBody] Boss boss)
    {
        boss.Id = id;
        var result = await _bossService.UpdateBossAsync(boss);
        if (result) return Ok();
        return BadRequest();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBossAsync(int id)
    {
        var result = await _bossService.DeleteBossAsync(id);
        if (result) return Ok();
        return BadRequest();
    }
}