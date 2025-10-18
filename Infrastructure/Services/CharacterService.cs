using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Services;

public class CharacterService : ICharacterService
{
    private readonly ICharacterRepository _characterRepository;
    private readonly ICharacterQuery _characterQuery;

    public CharacterService(ICharacterRepository characterRepository, ICharacterQuery characterQuery)
    {
        _characterRepository = characterRepository;
        _characterQuery = characterQuery;
    }

    public async Task<IEnumerable<Character>> GetByDiscordId(ulong discordId)
    {
        return await _characterQuery.GetByDiscordIdAsync(discordId);
    }

    public async Task<int> CreateAsync(Character character)
    {
        return await _characterRepository.CreateAsync(character);
    }

    public async Task<int> UpdateAsync(Character character)
    {
        return await _characterRepository.UpdateAsync(character);
    }

    public async Task<int> DeleteAsync(ulong discordId, string id)
    {
        return await _characterRepository.DeleteAsync(discordId, id);
    }
}