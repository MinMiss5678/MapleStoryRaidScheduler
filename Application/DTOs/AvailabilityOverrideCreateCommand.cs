namespace Application.DTOs;

/// <summary>新增一筆日期 override（DiscordId 由 Controller 從 Claims 注入，不驗）。</summary>
public class AvailabilityOverrideCreateCommand
{
    public ulong DiscordId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsAvailable { get; set; }
}
