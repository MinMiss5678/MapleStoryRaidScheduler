using Domain.Entities;

namespace Application.DTOs;

public class TeamSlotUpdateCommand
{
    public int Id { get; set; }
    public int BossId { get; set; }
    public int PeriodId { get; set; }
    public DateTimeOffset SlotDateTime { get; set; }
    public List<TeamSlotCharacter> Characters { get; set; } = new();
    public List<int> DeleteTeamSlotCharacterIds { get; set; } = new();
    public bool IsTemporary { get; set; }
    public bool IsPublished { get; set; }
    public int? TemplateId { get; set; }
}
