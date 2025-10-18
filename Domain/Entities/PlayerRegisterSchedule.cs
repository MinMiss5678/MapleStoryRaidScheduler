namespace Domain.Entities;

public class PlayerRegisterSchedule
{
    public int Id { get; set; }
    public ulong DiscordId { get; set; }
    public string DiscordName { get; set; }
    public string CharacterId { get; set; }
    public string CharacterName { get; set; }
    public string Job { get; set; }
    public int AttackPower { get; set; }
    public int[] Weekdays { get; set; }
    public string[] Timeslots { get; set; }
    public int Rounds { get; set; }
}