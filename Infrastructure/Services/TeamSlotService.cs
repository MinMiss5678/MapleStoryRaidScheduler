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

    public TeamSlotService(ITeamSlotRepository teamSlotRepository, ITeamSlotQuery teamSlotQuery,
        ITeamSlotCharacterRepository teamSlotCharacterRepository,
        IPeriodQuery periodQuery)
    {
        _teamSlotRepository = teamSlotRepository;
        _teamSlotQuery = teamSlotQuery;
        _teamSlotCharacterRepository = teamSlotCharacterRepository;
        _periodQuery = periodQuery;
    }

    public async Task<IEnumerable<TeamSlot>> GetByBossIdAsync(int bossId)
    {
        var period = await _periodQuery.GetByNowAsync();
        var teamSlotCharacters = await _teamSlotQuery.GetByPeriodAndBossIdAsync(period, bossId);

        return MapToTeamSlots(teamSlotCharacters, period, bossId);
    }

    public async Task<IEnumerable<TeamSlot>> GetByDiscordIdAsync(ulong discordId)
    {
        var period = await _periodQuery.GetByNowAsync();
        var teamSlotCharacters = await _teamSlotQuery.GetByPeriodAndDiscordIdAsync(period, discordId);

        return MapToTeamSlots(teamSlotCharacters, period);
    }

    private static List<TeamSlot> MapToTeamSlots(
        IEnumerable<TeamSlotCharacterDto> teamSlotCharacters, Period? period, int? defaultBossId = null)
    {
        return teamSlotCharacters
            .GroupBy(r => new { r.SlotDateTime, r.TeamSlotId })
            .Select(g => new TeamSlot
            {
                Id = g.Key.TeamSlotId,
                BossId = defaultBossId ?? g.FirstOrDefault()?.BossId ?? 0,
                PeriodId = period?.Id ?? 0,
                BossName = g.FirstOrDefault()?.BossName,
                SlotDateTime = g.Key.SlotDateTime,
                Characters = g.Select(x => new TeamSlotCharacter
                {
                    Id = x.TeamSlotCharacterId,
                    DiscordId = x.DiscordId,
                    DiscordName = x.DiscordName,
                    CharacterId = x.CharacterId,
                    CharacterName = x.CharacterName,
                    Job = x.Job,
                    AttackPower = x.AttackPower,
                    Rounds = x.Rounds,
                    TeamSlotId = x.TeamSlotId
                }).ToList()
            })
            .ToList();
    }

    public async Task UpdateAsync(TeamSlotUpdateRequest teamSlotUpdateRequest, bool isAdmin, ulong currentDiscordId)
    {
        if (teamSlotUpdateRequest.DeleteTeamSlotIds.Any())
        {
            if (!isAdmin)
                throw new UnauthorizedAccessException("只有管理員可以刪除隊伍。");

            foreach (var deleteId in teamSlotUpdateRequest.DeleteTeamSlotIds)
            {
                await _teamSlotCharacterRepository.DeleteByTeamSlotIdAsync(deleteId);
                await _teamSlotRepository.DeleteAsync(deleteId);
            }
        }

        foreach (var teamSlot in teamSlotUpdateRequest.TeamSlots)
        {
            if (teamSlot.IsTemporary)
            {
                if (!isAdmin)
                    throw new UnauthorizedAccessException("只有管理員可以建立新隊伍。");

                var entity = new TeamSlot
                {
                    BossId = teamSlot.BossId,
                    PeriodId = teamSlot.PeriodId,
                    SlotDateTime = teamSlot.SlotDateTime,
                    IsTemporary = teamSlot.IsTemporary,
                    IsPublished = teamSlot.IsPublished,
                    TemplateId = teamSlot.TemplateId
                };
                var teamSlotId = await _teamSlotRepository.CreateAsync(entity);
                foreach (var character in teamSlot.Characters)
                {
                    character.TeamSlotId = teamSlotId;
                    await _teamSlotCharacterRepository.CreateAsync(character);
                }

                continue;
            }

            var originalTeam = await _teamSlotRepository.GetByIdAsync(teamSlot.Id);
            if (originalTeam == null) continue;

            foreach (var teamSlotCharacterId in teamSlot.DeleteTeamSlotCharacterIds)
            {
                if (!isAdmin)
                {
                    // 一般玩家：只能刪除屬於自己的角色
                    var charToDelete = originalTeam.Characters.FirstOrDefault(c => c.Id == teamSlotCharacterId);
                    if (charToDelete != null && charToDelete.DiscordId != currentDiscordId)
                        throw new UnauthorizedAccessException("您不能移除他人的角色。");
                }

                var teamSlotCharacter = new TeamSlotCharacter()
                {
                    Id = teamSlotCharacterId,
                    TeamSlotId = teamSlot.Id,
                };

                await _teamSlotCharacterRepository.DeleteCharacterAsync(teamSlotCharacter);
            }

            foreach (var character in teamSlot.Characters)
            {
                if (character.Id == null)
                {
                    if (!isAdmin && character.DiscordId != currentDiscordId)
                        throw new UnauthorizedAccessException("不能替他人新增角色");

                    // 防止同一角色重複加入同一隊伍
                    if (character.CharacterId != null &&
                        originalTeam.Characters.Any(c => c.CharacterId == character.CharacterId))
                        throw new InvalidOperationException("該角色已在此隊伍中，不可重複加入。");

                    character.TeamSlotId = teamSlot.Id;
                    await _teamSlotCharacterRepository.CreateAsync(character);
                }
                else
                {
                    var originalCharacter = originalTeam.Characters.FirstOrDefault(c => c.Id == character.Id);
                    if (!isAdmin)
                    {
                        if (originalCharacter == null)
                            throw new UnauthorizedAccessException("找不到要修改的角色位");

                        // 允許修改自己的角色，或是填補空位 (CharacterId == null)
                        if (originalCharacter.DiscordId != currentDiscordId &&
                            originalCharacter.CharacterId != null && originalCharacter.DiscordId == 0)
                            throw new UnauthorizedAccessException("不能修改他人的角色");

                        // 確保填補空位時，填入的是自己的角色
                        if (originalCharacter.CharacterId == null && character.DiscordId != currentDiscordId &&
                            character.DiscordId != 0)
                            throw new UnauthorizedAccessException("填補空位時，必須填入自己的角色。");
                    }

                    await _teamSlotCharacterRepository.UpdateAsync(character);
                }
            }
        }
    }
}
