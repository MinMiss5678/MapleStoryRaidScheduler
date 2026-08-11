namespace Application.DTOs;

/// <summary>候選比對用的日期 override 投影（period-less §8 Phase 2b）。</summary>
public class AvailabilityOverrideItem
{
    public ulong DiscordId { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsAvailable { get; set; }
}
