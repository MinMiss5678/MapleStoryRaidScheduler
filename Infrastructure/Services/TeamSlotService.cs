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
    private readonly IBossRepository _bossRepository;

    public TeamSlotService(ITeamSlotRepository teamSlotRepository, ITeamSlotQuery teamSlotQuery,
        ITeamSlotCharacterRepository teamSlotCharacterRepository,
        IPeriodQuery periodQuery,
        IBossRepository bossRepository)
    {
        _teamSlotRepository = teamSlotRepository;
        _teamSlotQuery = teamSlotQuery;
        _teamSlotCharacterRepository = teamSlotCharacterRepository;
        _periodQuery = periodQuery;
        _bossRepository = bossRepository;
    }

    public async Task<IEnumerable<TeamSlotDto>> GetByBossIdAsync(int bossId)
    {
        var period = await _periodQuery.GetActivePeriodAsync();
        if (period == null) return [];
        var teamSlotCharacters = await _teamSlotQuery.GetByPeriodAndBossIdAsync(period, bossId);

        return MapToTeamSlotDtos(teamSlotCharacters, period, bossId);
    }

    public async Task<IEnumerable<TeamSlotDto>> GetByDiscordIdAsync(ulong discordId)
    {
        var period = await _periodQuery.GetActivePeriodAsync();
        if (period == null) return [];
        var teamSlotCharacters = await _teamSlotQuery.GetByPeriodAndDiscordIdAsync(period, discordId);

        return MapToTeamSlotDtos(teamSlotCharacters, period);
    }

    private static List<TeamSlotDto> MapToTeamSlotDtos(
        IEnumerable<TeamSlotCharacterDto> teamSlotCharacters, Period? period, int? defaultBossId = null)
    {
        return teamSlotCharacters
            .GroupBy(r => new { r.SlotDateTime, r.TeamSlotId })
            .Select(g => new TeamSlotDto
            {
                Id = g.Key.TeamSlotId,
                BossId = defaultBossId ?? g.FirstOrDefault()?.BossId ?? 0,
                PeriodId = period?.Id ?? 0,
                BossName = g.FirstOrDefault()?.BossName,
                SlotDateTime = g.Key.SlotDateTime,
                // LEFT JOIN 在隊伍無成員時會產生 TeamSlotCharacterId=0 的 ghost row，需過濾掉
                Characters = g.Where(x => x.TeamSlotCharacterId != 0)
                    .Select(x => new TeamSlotMemberDto
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

        // 容量 = Boss.RequireMembers；一次撈全部、迴圈內查表，避免逐筆 teamSlot 各打一次 N+1。
        var bossesById = (await _bossRepository.GetAllAsync()).ToDictionary(b => b.Id);

        foreach (var teamSlot in teamSlotUpdateRequest.TeamSlots)
        {
            // 負 Id / 0 = 尚未存檔的新隊 → CREATE；正 Id = 既有隊 → UPDATE
            if (teamSlot.Id <= 0)
            {
                if (!isAdmin)
                    throw new UnauthorizedAccessException("只有管理員可以建立新隊伍。");

                var entity = new TeamSlot
                {
                    BossId = teamSlot.BossId,
                    PeriodId = teamSlot.PeriodId,
                    SlotDateTime = teamSlot.SlotDateTime,
                    Source = teamSlot.Source,
                    TemplateId = teamSlot.TemplateId
                };
                var teamSlotId = await _teamSlotRepository.CreateAsync(entity);
                foreach (var member in teamSlot.Characters)
                {
                    var newChar = MapToEntity(member);
                    newChar.TeamSlotId = teamSlotId;
                    await _teamSlotCharacterRepository.CreateAsync(newChar);
                }

                continue;
            }

            var originalTeam = await _teamSlotRepository.GetByIdAsync(teamSlot.Id);
            if (originalTeam == null) continue;

            // 不變式需要 Capacity 才守得住（見 TeamSlot.HasRoom/AddMember）
            originalTeam.Capacity = bossesById.TryGetValue(originalTeam.BossId, out var boss) ? boss.RequireMembers : 6;

            foreach (var teamSlotCharacterId in teamSlot.DeleteTeamSlotCharacterIds)
            {
                if (!isAdmin)
                {
                    // 一般玩家：只能刪除屬於自己的角色
                    var charToDelete = originalTeam.Characters.FirstOrDefault(c => c.Id == teamSlotCharacterId);
                    if (charToDelete != null && charToDelete.DiscordId != currentDiscordId)
                        throw new UnauthorizedAccessException("您不能移除他人的角色。");
                }

                await _teamSlotCharacterRepository.DeleteCharacterAsync(new TeamSlotCharacter
                {
                    Id = teamSlotCharacterId,
                    TeamSlotId = teamSlot.Id,
                    DiscordName = "",
                    Job = ""
                });
            }

            foreach (var member in teamSlot.Characters)
            {
                if (member.Id == null)
                {
                    if (!isAdmin && member.DiscordId != currentDiscordId)
                        throw new UnauthorizedAccessException("不能替他人新增角色");

                    var newChar = MapToEntity(member);
                    newChar.TeamSlotId = teamSlot.Id;
                    // IsManual 由來源端顯式決定（玩家補位/管理員微調=true，重排自動填=false），後端不強制。
                    // 守隊伍不變式：擋重複加入、擋超額（含 admin，違反丟 DomainException → 400）。
                    originalTeam.AddMember(newChar);
                    await _teamSlotCharacterRepository.CreateAsync(newChar);
                }
                else
                {
                    var originalCharacter = originalTeam.Characters.FirstOrDefault(c => c.Id == member.Id);
                    if (!isAdmin)
                    {
                        if (originalCharacter == null)
                            throw new UnauthorizedAccessException("找不到要修改的角色位");

                        // 允許修改自己的角色，或是填補空位 (CharacterId == null)
                        if (originalCharacter.DiscordId != currentDiscordId &&
                            originalCharacter.CharacterId != null && originalCharacter.DiscordId == 0)
                            throw new UnauthorizedAccessException("不能修改他人的角色");

                        // 確保填補空位時，填入的是自己的角色
                        if (originalCharacter.CharacterId == null && member.DiscordId != currentDiscordId &&
                            member.DiscordId != 0)
                            throw new UnauthorizedAccessException("填補空位時，必須填入自己的角色。");
                    }

                    await _teamSlotCharacterRepository.UpdateAsync(MapToEntity(member));
                }
            }
        }
    }

    private static TeamSlotCharacter MapToEntity(TeamSlotMemberDto dto) => new()
    {
        Id = dto.Id,
        TeamSlotId = dto.TeamSlotId,
        DiscordId = dto.DiscordId,
        DiscordName = dto.DiscordName,
        CharacterId = dto.CharacterId,
        CharacterName = dto.CharacterName,
        Job = dto.Job,
        AttackPower = dto.AttackPower,
        Level = dto.Level,
        Rounds = dto.Rounds,
        IsManual = dto.IsManual
    };
}
