namespace Domain.Entities;

public class BossTemplateRequirement
{
    public int Id { get; set; }
    public int BossTemplateId { get; set; }
    public string JobCategory { get; set; }
    public int Count { get; set; }
    public int Priority { get; set; } // 優先級，排團時先填滿高優先級
    
    public int? MinLevel { get; set; } // 最低等級門檻
    public int? MinAttribute { get; set; } // 最低屬性值 (如 AP)
    public bool IsOptional { get; set; } // 是否為選配/盡量安排
    public string? Description { get; set; } // UI 顯示的提醒文字或邏輯說明
}
