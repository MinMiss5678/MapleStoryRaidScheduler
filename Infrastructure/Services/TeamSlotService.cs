using Application.DTOs;
using Application.Exceptions;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;

namespace Infrastructure.Services;

public class TeamSlotService : ITeamSlotService
{
    private readonly ITeamSlotRepository _teamSlotRepository;
    private readonly ITeamSlotQuery _teamSlotQuery;
    private readonly ITeamSlotCharacterRepository _teamSlotCharacterRepository;
    private readonly IPeriodQuery _periodQuery;
    private readonly IBossRepository _bossRepository;
    private readonly IRegistrationLock _registrationLock;
    private readonly ICharacterQuery _characterQuery;

    public TeamSlotService(ITeamSlotRepository teamSlotRepository, ITeamSlotQuery teamSlotQuery,
        ITeamSlotCharacterRepository teamSlotCharacterRepository,
        IPeriodQuery periodQuery,
        IBossRepository bossRepository,
        IRegistrationLock registrationLock,
        ICharacterQuery characterQuery)
    {
        _teamSlotRepository = teamSlotRepository;
        _teamSlotQuery = teamSlotQuery;
        _teamSlotCharacterRepository = teamSlotCharacterRepository;
        _periodQuery = periodQuery;
        _bossRepository = bossRepository;
        _registrationLock = registrationLock;
        _characterQuery = characterQuery;
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
                        TeamSlotId = x.TeamSlotId,
                        Version = x.Version
                    }).ToList()
            })
            .ToList();
    }

    public async Task<TeamSlotUpdateResult> UpdateAsync(TeamSlotUpdateRequest teamSlotUpdateRequest, bool isAdmin, ulong currentDiscordId)
    {
        // 統一衝突回報：隊伍消失（merge/自動排團砍掉重灌）或樂觀鎖版本衝突都塞進這份清單，
        // 不中斷其他隊伍的處理——未列在清單裡的隊伍皆已成功存檔。
        var conflicts = new List<int>();

        if (teamSlotUpdateRequest.DeleteTeamSlotIds.Any())
        {
            if (!isAdmin)
                throw new ForbiddenException("只有管理員可以刪除隊伍。");

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
                    throw new ForbiddenException("只有管理員可以建立新隊伍。");

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

            var originalTeam = await AcquireAndLoadTeamSlotAsync(teamSlot.Id, conflicts);
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
                        throw new ForbiddenException("您不能移除他人的角色。");
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
                        throw new ForbiddenException("不能替他人新增角色");

                    var newChar = MapToEntity(member);
                    newChar.TeamSlotId = teamSlot.Id;
                    // IsManual 由來源端顯式決定（玩家補位/管理員微調=true，重排自動填=false），後端不強制。
                    await AddCharacterToTeamAsync(originalTeam, newChar);
                }
                else
                {
                    var originalCharacter = originalTeam.Characters.FirstOrDefault(c => c.Id == member.Id);
                    if (!isAdmin)
                    {
                        if (originalCharacter == null)
                            throw new NotFoundException("找不到要修改的角色位");

                        // 允許修改自己的角色，或是填補空位 (CharacterId == null)
                        if (originalCharacter.DiscordId != currentDiscordId &&
                            originalCharacter.CharacterId != null)
                            throw new ForbiddenException("不能修改他人的角色");

                        // 確保填補空位時，填入的是自己的角色
                        if (originalCharacter.CharacterId == null && member.DiscordId != currentDiscordId &&
                            member.DiscordId != 0)
                            throw new ForbiddenException("填補空位時，必須填入自己的角色。");
                    }

                    var updated = await _teamSlotCharacterRepository.UpdateAsync(MapToEntity(member));
                    if (!updated)
                        conflicts.Add(teamSlot.Id);
                }
            }
        }

        return new TeamSlotUpdateResult { ConflictedTeamSlotIds = conflicts.Distinct().ToList() };
    }

    /// <summary>
    /// 玩家補位：把自己的角色加進某個空位。跟 UpdateAsync 不同，payload 型別上放不進別人的資料
    /// （DiscordId 一律用 currentDiscordId，不信任 client），不需要、也沒有擁有權檢查可寫錯。
    /// </summary>
    public async Task<TeamSlotDto> FillSlotAsync(TeamSlotFillRequest request, ulong currentDiscordId)
    {
        var conflicts = new List<int>();
        var originalTeam = await AcquireAndLoadTeamSlotAsync(request.TeamSlotId, conflicts);
        if (originalTeam == null)
            throw new BusinessException("隊伍目前無法補位，請重新整理後再試");

        var boss = await _bossRepository.GetByIdAsync(originalTeam.BossId);
        originalTeam.Capacity = boss?.RequireMembers ?? 6;

        // 補位角色必須屬於補位者本人：擋冒用他人角色 id + Character FK 後防（不存在）→ 404。
        // 見 plans/2026-08-06-validation-layering.md §2；DiscordId 已強制用 currentDiscordId，故只需驗角色歸屬。
        var ownsCharacter = (await _characterQuery.GetByDiscordIdAsync(currentDiscordId))
            .Any(c => c.Id == request.CharacterId);
        if (!ownsCharacter)
            throw new NotFoundException($"Character {request.CharacterId} not found");

        var newChar = new TeamSlotCharacter
        {
            TeamSlotId = request.TeamSlotId,
            DiscordId = currentDiscordId,
            DiscordName = request.DiscordName ?? "",
            CharacterId = request.CharacterId,
            CharacterName = request.CharacterName,
            Job = request.Job,
            AttackPower = request.AttackPower,
            Rounds = request.Rounds,
            IsManual = true // 玩家補位＝人工調整，重排時受保護
        };

        await AddCharacterToTeamAsync(originalTeam, newChar);

        // 重新查詢最新狀態（含新角色的真實 Id/Version）回給前端：跟 UpdateAsync controller 的既有慣例一致
        // （寫入後重查、包進同一個回應），不是額外一支 API——CreateAsync 用的泛用 DapperRepository.InsertAsync
        // 只回受影響列數、拿不到自動產生的 Id，用重新查詢換取正確性最省事、風險最低。
        var teamSlots = await GetByBossIdAsync(originalTeam.BossId);
        var updatedTeamSlot = teamSlots.FirstOrDefault(t => t.Id == request.TeamSlotId);
        if (updatedTeamSlot == null)
            throw new BusinessException("補位成功，但目前查無最新隊伍資料，請重新整理頁面");

        return updatedTeamSlot;
    }

    /// <summary>
    /// 取鎖＋撈隊伍，兩邊共用（UpdateAsync 批次處理 / FillSlotAsync 單一操作）。
    /// 取不到鎖（lock_timeout）或隊伍已消失都記進 conflicts、回傳 null，呼叫端自行決定
    /// 「跳過繼續處理其他隊伍」（批次）或「整個操作視為失敗」（單一）。
    /// </summary>
    private async Task<TeamSlot?> AcquireAndLoadTeamSlotAsync(int teamSlotId, List<int> conflicts)
    {
        try
        {
            await _registrationLock.AcquireTeamSlotEditLockAsync(teamSlotId);
        }
        catch (AdvisoryLockTimeoutException)
        {
            conflicts.Add(teamSlotId);
            return null;
        }

        var team = await _teamSlotRepository.GetByIdAsync(teamSlotId);
        if (team == null)
            conflicts.Add(teamSlotId);

        return team;
    }

    /// <summary>
    /// 新增成員的核心邏輯，UpdateAsync／FillSlotAsync 共用：守隊伍不變式（擋重複加入、擋超額，
    /// 違反丟 DomainException → 400），再寫入 DB。呼叫端已各自處理好授權判斷。
    /// </summary>
    private async Task AddCharacterToTeamAsync(TeamSlot team, TeamSlotCharacter newChar)
    {
        team.AddMember(newChar);
        await _teamSlotCharacterRepository.CreateAsync(newChar);
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
        IsManual = dto.IsManual,
        Version = dto.Version
    };
}
