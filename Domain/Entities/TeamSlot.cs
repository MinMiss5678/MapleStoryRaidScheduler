namespace Domain.Entities;

public class TeamSlot
{
    public int Id { get; set; }
    public int BossId { get; set; }
    public string? BossName { get; set; }
    public DateTimeOffset SlotDateTime { get; set; }
    public List<TeamSlotCharacter> Characters { get; set; } = new();
    public List<string> DeleteCharacterIds { get; set; } = new();
    public bool IsTemporary { get; set; }
}