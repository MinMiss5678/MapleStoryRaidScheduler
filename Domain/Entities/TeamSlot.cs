namespace Domain.Entities;

public class TeamSlot
{
    public int Id { get; set; }
    public int BossId { get; set; }
    public int PeriodId { get; set; }
    public string? BossName { get; set; }
    public DateTimeOffset SlotDateTime { get; set; }
    public List<TeamSlotCharacter> Characters { get; set; } = new();
    public List<int> DeleteTeamSlotCharacterIds { get; set; } = new();
    public bool IsTemporary { get; set; }
    public bool IsPublished { get; set; } // 管理員發佈後玩家才能補位
    public int? TemplateId { get; set; }
}