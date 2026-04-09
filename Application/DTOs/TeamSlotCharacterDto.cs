namespace Application.DTOs;

public class TeamSlotCharacterDto
{
    public int TeamSlotId { get; set; }
    public int TeamSlotCharacterId { get; set; }
    public int BossId { get; set; }
    public string BossName { get; set; }
    public DateTimeOffset SlotDateTime { get; set; }
    public ulong DiscordId { get; set; }
    public string DiscordName { get; set; }
    public string? CharacterId { get; set; }
    public string? CharacterName { get; set; }
    public string Job { get; set; }
    public int AttackPower { get; set; }
    public int Rounds { get; set; }
}