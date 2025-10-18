using Domain.Entities;

namespace Application.DTOs;

public class TeamSlotUpdateRequest
{
    public int BossId { get; set; }
    public List<TeamSlot> TeamSlots { get; set; }
    public List<int> DeleteTeamSlotIds { get; set; }
}