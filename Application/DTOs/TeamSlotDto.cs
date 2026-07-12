namespace Application.DTOs;

public class TeamSlotDto
{
    public int Id { get; set; }
    public int BossId { get; set; }
    public int PeriodId { get; set; }
    public string? BossName { get; set; }
    public DateTimeOffset SlotDateTime { get; set; }
    public List<TeamSlotMemberDto> Characters { get; set; } = new();
    public string Source { get; set; } = Domain.Entities.TeamSlotSource.Auto;
    public int? TemplateId { get; set; }
}
