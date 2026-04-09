namespace Domain.Entities;

public class PlayerAvailability
{
    public int Id { get; set; }
    public ulong DiscordId { get; set; }
    public int PlayerRegisterId { get; set; }
    public int Weekday { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
