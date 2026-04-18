namespace Application.DTOs;

public class BossTemplateRequest
{
    public int BossId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<BossTemplateRequirementRequest> Requirements { get; set; } = [];
}

public class BossTemplateRequirementRequest
{
    public string JobCategory { get; set; } = string.Empty;
    public int Count { get; set; }
    public int Priority { get; set; }
    public int? MinLevel { get; set; }
    public int? MinAttribute { get; set; }
    public bool IsOptional { get; set; }
    public string? Description { get; set; }
}
