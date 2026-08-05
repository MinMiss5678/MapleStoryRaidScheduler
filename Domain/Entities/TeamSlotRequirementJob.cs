namespace Domain.Entities;

/// <summary>
/// 需求列的可接受職業之一，攻擊下限下放到職業層級（同攻擊下不同職業傷害期望不同）。
/// 分類在存檔時已展開成具體職業（快照），故此處存的是具體 Job。見計畫 §3 / §9.15。
/// </summary>
public class TeamSlotRequirementJob
{
    public int Id { get; set; }
    public int RequirementId { get; set; }
    public required string Job { get; set; }
    public int MinAttackPower { get; set; }   // 以無BUFF base 計（見計畫 §9.16）
}
