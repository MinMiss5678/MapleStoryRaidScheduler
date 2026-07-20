namespace Domain.Entities;

public class PlayerRegisterSchedule
{
    public int Id { get; set; }
    public ulong DiscordId { get; set; }
    public required string DiscordName { get; set; }
    public required string CharacterId { get; set; }
    public required string CharacterName { get; set; }
    public required string Job { get; set; }
    public int AttackPower { get; set; }
    public int Level { get; set; }
    public List<PlayerAvailability> Availabilities { get; set; } = [];
    public int Rounds { get; set; }
}