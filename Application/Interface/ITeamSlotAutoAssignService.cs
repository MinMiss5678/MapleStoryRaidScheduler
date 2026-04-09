using Domain.Entities;

namespace Application.Interface;

public interface ITeamSlotAutoAssignService
{
    Task AutoAssignAsync(Register register);
}
