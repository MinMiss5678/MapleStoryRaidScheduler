using Application.DTOs;

namespace Application.Interface;

public interface ICharacterService
{
    Task<IEnumerable<CharacterDto>> GetWithDiscordNameAsync(ulong discordId, int? bossId = null);
    Task<int> CreateAsync(CharacterRequest request);
    Task UpdateAsync(CharacterRequest request);
    Task DeleteAsync(ulong discordId, string id);
}
