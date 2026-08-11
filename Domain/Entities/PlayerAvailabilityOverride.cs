namespace Domain.Entities;

/// <summary>
/// 可用時段的日期 override（period-less §8 Phase 2b）：疊在常設之上，針對特定日期標例外。
/// IsAvailable=false → 該日該時段不行（蓋掉常設）；true → 額外加開。候選比對時 override 勝過常設。
/// </summary>
public class PlayerAvailabilityOverride
{
    public int Id { get; set; }
    public ulong DiscordId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsAvailable { get; set; }
}
