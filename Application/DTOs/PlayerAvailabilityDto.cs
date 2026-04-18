namespace Application.DTOs;

public class PlayerAvailabilityDto
{
    public int Weekday { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
