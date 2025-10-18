using Domain.Entities;

namespace Domain.Repositories;

public interface ICharacterRepository
{
    Task<int> CreateAsync(Character character);
    Task<int> UpdateAsync(Character character);
    Task<int> DeleteAsync(ulong discordId, string id);
}