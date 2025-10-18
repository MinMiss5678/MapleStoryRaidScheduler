using Domain.Entities;

namespace Application.Interface;

public interface ICharacterService
{
    Task<IEnumerable<Character>> GetByDiscordId(ulong discordId);
    Task<int> CreateAsync(Character character);
    Task<int> UpdateAsync(Character character);
    Task<int> DeleteAsync(ulong discordId, string id);
}