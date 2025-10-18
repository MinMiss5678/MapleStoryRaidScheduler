namespace Domain.Entities;

public class TeamSlotCharacter
{
    public int? Id { get; set; }
    public int TeamSlotId { get; set; }
    public ulong DiscordId { get; set; }
    public string DiscordName { get; set; }
    public string CharacterId { get; set; }
    public string? CharacterName { get; set; }
    public string Job { get; set; }
    public int AttackPower { get; set; }
    public int Rounds { get; set; }
}