namespace Application.DTOs;

public class TeamSlotUpdateRequest
{
    public int BossId { get; set; }
    public required List<TeamSlotUpdateCommand> TeamSlots { get; set; }
    public required List<int> DeleteTeamSlotIds { get; set; }
}