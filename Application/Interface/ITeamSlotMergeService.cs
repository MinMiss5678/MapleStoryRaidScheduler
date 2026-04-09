using Domain.Entities;

namespace Application.Interface;

public interface ITeamSlotMergeService
{
    Task MergeTeamsAsync(Register register);
}
