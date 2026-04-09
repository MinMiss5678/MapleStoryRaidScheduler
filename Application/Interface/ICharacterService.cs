using Application.DTOs;
using Domain.Entities;

namespace Application.Interface;

public interface ICharacterService
{
    Task<IEnumerable<CharacterDto>> GetWithDiscordNameAsync(ulong discordId, int? bossId = null);
    Task<int> CreateAsync(Character character);
    Task<int> UpdateAsync(Character character);
    Task<int> DeleteAsync(ulong discordId, string id);
}