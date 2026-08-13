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
    private readonly ICharacterBossClearRepository _characterBossClearRepository;

    public CharacterService(ICharacterRepository characterRepository, ICharacterQuery characterQuery, ICharacterBossClearRepository characterBossClearRepository)
    {
        _characterRepository = characterRepository;
        _characterQuery = characterQuery;
        _characterBossClearRepository = characterBossClearRepository;
    }

    public async Task<IEnumerable<CharacterDto>> GetWithDiscordNameAsync(ulong discordId, int? bossId = null)
    {
        return await _characterQuery.GetWithDiscordNameAsync(discordId, bossId);
    }

    public async Task<int> CreateAsync(CharacterRequest request)
    {
        var character = new Character
        {
            Id = request.Id,
            DiscordId = request.DiscordId,
            Name = request.Name,
            Job = request.Job,
            AttackPower = request.AttackPower
        };
        return await _characterRepository.CreateAsync(character);
    }

    public async Task UpdateAsync(CharacterRequest request)
    {
        var character = new Character
        {
            Id = request.Id,
            DiscordId = request.DiscordId,
            Name = request.Name,
            Job = request.Job,
            AttackPower = request.AttackPower
        };
        var rows = await _characterRepository.UpdateAsync(character);
        if (rows == 0) throw new NotFoundException($"Character {request.Id} not found");
    }

    public async Task DeleteAsync(ulong discordId, string id)
    {
        // 先清該角色的通關數（FK 到 Character），再刪角色。
        await _characterBossClearRepository.DeleteByCharacterIdAsync(id);
        var rows = await _characterRepository.DeleteAsync(discordId, id);
        if (rows == 0) throw new NotFoundException($"Character {id} not found");
    }

    public async Task<IEnumerable<BossClearDto>> GetBossClearsAsync(ulong discordId, string characterId)
    {
        await EnsureOwnedAsync(discordId, characterId);
        var clears = await _characterBossClearRepository.GetByCharacterIdAsync(characterId);
        return clears.Select(c => new BossClearDto { BossId = c.BossId, ClearCount = c.ClearCount });
    }

    public async Task SaveBossClearsAsync(ulong discordId, string characterId, IEnumerable<BossClearDto> clears)
    {
        await EnsureOwnedAsync(discordId, characterId);
        foreach (var c in clears)
            await _characterBossClearRepository.UpsertAsync(new CharacterBossClear
            {
                CharacterId = characterId,
                BossId = c.BossId,
                ClearCount = c.ClearCount
            });
    }

    // 角色必須屬於登入者，否則不得讀寫其通關數（角色 Id 由 client 傳、不可信）。
    private async Task EnsureOwnedAsync(ulong discordId, string characterId)
    {
        var owned = await _characterQuery.GetByDiscordIdAsync(discordId);
        if (owned.All(c => c.Id != characterId))
            throw new NotFoundException($"Character {characterId} not found");
    }
}
