using Application.DTOs;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Services;

public class TeamSlotService : ITeamSlotService
{
    private readonly ITeamSlotRepository _teamSlotRepository;
    private readonly ITeamSlotQuery _teamSlotQuery;
    private readonly ITeamSlotCharacterRepository _teamSlotCharacterRepository;
    private readonly IPeriodQuery _periodQuery;
    private readonly IUnitOfWork _unitOfWork;

    public TeamSlotService(ITeamSlotRepository teamSlotRepository, ITeamSlotQuery teamSlotQuery,
        ITeamSlotCharacterRepository teamSlotCharacterRepository, IPeriodQuery periodQuery, IUnitOfWork unitOfWork)
    {
        _teamSlotRepository = teamSlotRepository;
        _teamSlotQuery = teamSlotQuery;
        _teamSlotCharacterRepository = teamSlotCharacterRepository;
        _periodQuery = periodQuery;
        _unitOfWork = unitOfWork;
    }

    public async Task<IEnumerable<TeamSlot>> GetByBossIdAsync(int bossId)
    {
        var period = await _periodQuery.GetByNowAsync();
        var teamSlotCharacters = await _teamSlotQuery.GetByPeriodAndBossIdAsync(period, bossId);

        var teamSlotCharacterId = 1;
        var result = teamSlotCharacters
            .GroupBy(r => new { r.SlotDateTime })
            .Select(g => new TeamSlot
            {
                Id = g.FirstOrDefault().TeamSlotId,
                BossId = bossId,
                SlotDateTime = g.Key.SlotDateTime,
                Characters = g.Select(x => new TeamSlotCharacter
                {
                    Id = teamSlotCharacterId++,
                    DiscordId = x.DiscordId,
                    DiscordName = x.DiscordName,
                    CharacterId = x.CharacterId,
                    CharacterName = x.CharacterName,
                    Job = x.Job,
                    AttackPower = x.AttackPower
                }).ToList()
            })
            .ToList();

        return result;
    }

    public async Task<IEnumerable<TeamSlot>> GetByDiscordIdAsync(ulong discordId)
    {
        var period = await _periodQuery.GetByNowAsync();
        var teamSlotCharacters = await _teamSlotQuery.GetByPeriodAndDiscordIdAsync(period, discordId);

        var teamSlotCharacterId = 1;
        var result = teamSlotCharacters
            .GroupBy(r => new { r.SlotDateTime })
            .Select(g => new TeamSlot
            {
                Id = g.FirstOrDefault().TeamSlotId,
                BossName = g.FirstOrDefault().BossName,
                SlotDateTime = g.Key.SlotDateTime,
                Characters = g.Select(x => new TeamSlotCharacter
                {
                    Id = teamSlotCharacterId++,
                    DiscordId = x.DiscordId,
                    DiscordName = x.DiscordName,
                    CharacterId = x.CharacterId,
                    CharacterName = x.CharacterName,
                    Job = x.Job,
                    AttackPower = x.AttackPower
                }).ToList()
            })
            .ToList();

        return result;
    }

    public async Task UpdateAsync(TeamSlotUpdateRequest teamSlotUpdateRequest)
    {
        await _unitOfWork.BeginTransactionAsync();

        try
        {
            foreach (var deleteId in teamSlotUpdateRequest.DeleteTeamSlotIds)
            {
                await _teamSlotCharacterRepository.DeleteByTeamSlotIdAsync(deleteId);
                await _teamSlotRepository.DeleteAsync(deleteId);
            }

            foreach (var teamSlot in teamSlotUpdateRequest.TeamSlots)
            {
                if (teamSlot.IsTemporary)
                {
                    teamSlot.SlotDateTime = teamSlot.SlotDateTime.ToUniversalTime();
                    var teamSlotId = await _teamSlotRepository.CreateAsync(teamSlot);
                    foreach (var character in teamSlot.Characters)
                    {
                        character.TeamSlotId = teamSlotId;
                        await _teamSlotCharacterRepository.CreateAsync(character);
                    }

                    continue;
                }

                foreach (var characterId in teamSlot.DeleteCharacterIds)
                {
                    var teamSlotCharacter = new TeamSlotCharacter()
                    {
                        TeamSlotId = teamSlot.Id,
                        CharacterId = characterId
                    };

                    await _teamSlotCharacterRepository.DeleteCharacterAsync(teamSlotCharacter);
                }

                foreach (var character in teamSlot.Characters)
                {
                    if (character.Id == null)
                    {
                        character.TeamSlotId = teamSlot.Id;
                        await _teamSlotCharacterRepository.CreateAsync(character);
                    }
                }
            }

            await _unitOfWork.CommitAsync();
        }
        catch (Exception e)
        {
            await _unitOfWork.RollbackAsync();
            throw;
        }
    }
}