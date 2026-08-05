namespace Domain.Entities;

/// <summary>
/// 隊伍條件（掛 TeamSlot 實例，非範本）：一組可接受職業（各帶攻擊下限）+ 數量 + 通關數門檻。
/// 「箭神(≥900) or 槍神(≥1000) 1位」= Count=1 + Jobs{(箭神,900),(槍神,1000)}。見計畫 §3。
/// </summary>
public class TeamSlotRequirement
{
    public int Id { get; set; }
    public int TeamSlotId { get; set; }
    public int Count { get; set; } = 1;        // 這列需要幾人
    public int MinClearCount { get; set; }      // 本王通關數門檻（0 = 不限）
    public List<TeamSlotRequirementJob> Jobs { get; set; } = [];  // 可接受職業（各帶自己的攻擊下限）
}
