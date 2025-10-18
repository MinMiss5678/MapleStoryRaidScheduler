namespace Domain.Entities;

public class TeamSlotDiscord
{
    public DateTimeOffset SlotDateTime { get; set; }
    public string BossName { get; set; }
    public List<ulong> DiscordIds { get; set; }
}