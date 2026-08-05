using System.ComponentModel.DataAnnotations;

namespace Application.DTOs;

public class BossTemplateRequest
{
    public int BossId { get; set; } // 存在性由 BossService.CreateTemplateAsync 驗（不存在回 404）

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    public List<BossTemplateRequirementRequest> Requirements { get; set; } = [];
}

public class BossTemplateRequirementRequest
{
    [Required]
    public string JobCategory { get; set; } = string.Empty;

    [Range(1, int.MaxValue)] // 每個需求至少要 1 個
    public int Count { get; set; }

    [Range(0, int.MaxValue)]
    public int Priority { get; set; }

    public int? MinLevel { get; set; }
    public int? MinAttribute { get; set; }
    public bool IsOptional { get; set; }
    public string? Description { get; set; }
}
