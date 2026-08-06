using Application.DTOs;
using Domain.Entities;

namespace Application.Queries;

public interface ICharacterQuery
{
    Task<IEnumerable<Character>> GetByDiscordIdAsync(ulong discordId);
    Task<Character?> GetByIdAsync(string id);
    Task<IEnumerable<CharacterDto>> GetWithDiscordNameAsync(ulong discordId, int? bossId = null);
}
