namespace Domain.Entities;

public class TeamSlot
{
    public int Id { get; set; }
    public int BossId { get; set; }
    public int PeriodId { get; set; }
    public string? BossName { get; set; }
    public DateTimeOffset SlotDateTime { get; set; }
    public List<TeamSlotCharacter> Characters { get; set; } = new();
    public string Source { get; set; } = TeamSlotSource.Auto;        // auto | admin，見 TeamSlotSource
    public int? TemplateId { get; set; }
}
