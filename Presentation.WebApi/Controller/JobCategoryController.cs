using Domain.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Presentation.WebApi.Controller;

[ApiController]
[Route("api/[controller]")]
public class JobCategoryController : ControllerBase
{
    private readonly IJobCategoryRepository _jobCategoryRepository;

    public JobCategoryController(IJobCategoryRepository jobCategoryRepository)
    {
        _jobCategoryRepository = jobCategoryRepository;
    }

    [HttpGet("GetJobMap")]
    public async Task<IActionResult> GetJobMapAsync()
    {
        var result = await _jobCategoryRepository.GetAllAsync();
        return Ok(result.ToDictionary(x => x.JobName, x => x.CategoryName));
    }
}
