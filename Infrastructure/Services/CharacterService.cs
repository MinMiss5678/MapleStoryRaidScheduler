using Application.DTOs;
using Application.Exceptions;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Services;

public class CharacterService : ICharacterService
{
    private readonly ICharacterRepository _characterRepository;
    private readonly ICharacterQuery _characterQuery;
    private readonly ICharacterRegisterRepository _characterRegisterRepository;

    public CharacterService(ICharacterRepository characterRepository, ICharacterQuery characterQuery, ICharacterRegisterRepository characterRegisterRepository)
    {
        _characterRepository = characterRepository;
        _characterQuery = characterQuery;
        _characterRegisterRepository = characterRegisterRepository;
    }

    public async Task<IEnumerable<CharacterDto>> GetWithDiscordNameAsync(ulong discordId, int? bossId = null)
    {
        return await _characterQuery.GetWithDiscordNameAsync(discordId, bossId);
    }

    public async Task<int> CreateAsync(Character character)
    {
        return await _characterRepository.CreateAsync(character);
    }

    public async Task UpdateAsync(Character character)
    {
        var rows = await _characterRepository.UpdateAsync(character);
        if (rows == 0) throw new NotFoundException($"Character {character.Id} not found");
    }

    public async Task DeleteAsync(ulong discordId, string id)
    {
        await _characterRegisterRepository.DeleteByCharacterIdAsync(id);
        var rows = await _characterRepository.DeleteAsync(discordId, id);
        if (rows == 0) throw new NotFoundException($"Character {id} not found");
    }
}