using System.ComponentModel.DataAnnotations;

namespace Application.DTOs;

public class BossRequest
{
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue)] // 隊伍容量至少 1，否則排團容量為 0
    public int RequireMembers { get; set; }

    // 消耗至少 1：0 會讓「場次預算」規則被繞過（rounds×0=0 永不超額）且前端 floor(remaining/0)=Infinity
    [Range(1, int.MaxValue)]
    public int RoundConsumption { get; set; } = 1;
}
