using Domain.Entities;

namespace Domain.Repositories;

public interface ITeamSlotRepository
{
    Task<int> CreateAsync(TeamSlot teamSlot);
    Task DeleteAsync(int id);
}