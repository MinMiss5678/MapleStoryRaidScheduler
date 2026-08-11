namespace Application.DTOs;

/// <summary>玩家自己的日期 override 一筆（GET 回傳；period-less §8 Phase 2b-write）。</summary>
public class AvailabilityOverrideDto
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsAvailable { get; set; }
}
