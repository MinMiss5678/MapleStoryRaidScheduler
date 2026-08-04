using System.ComponentModel.DataAnnotations;

namespace Application.DTOs;

public class TeamSlotUpdateRequest
{
    [Range(1, int.MaxValue)]
    public int BossId { get; set; }

    public required List<TeamSlotUpdateCommand> TeamSlots { get; set; }
    public required List<int> DeleteTeamSlotIds { get; set; }
}
