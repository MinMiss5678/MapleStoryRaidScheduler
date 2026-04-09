using Application.DTOs;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;

namespace Infrastructure.Services;

public class TeamSlotService : ITeamSlotService
{
    private readonly ITeamSlotRepository _teamSlotRepository;
    private readonly ITeamSlotQuery _teamSlotQuery;
    private readonly ITeamSlotCharacterRepository _teamSlotCharacterRepository;
    private readonly IPlayerAvailabilityRepository _playerAvailabilityRepository;
    private readonly IPeriodQuery _periodQuery;
    private readonly ICharacterQuery _characterQuery;
    private readonly IBossRepository _bossRepository;
    private readonly IJobCategoryRepository _jobCategoryRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DbContext _dbContext;

    public TeamSlotService(ITeamSlotRepository teamSlotRepository, ITeamSlotQuery teamSlotQuery,
        ITeamSlotCharacterRepository teamSlotCharacterRepository,
        IPlayerAvailabilityRepository playerAvailabilityRepository,
        IPeriodQuery periodQuery,
        ICharacterQuery characterQuery, IBossRepository bossRepository,
        IJobCategoryRepository jobCategoryRepository, IUnitOfWork unitOfWork, DbContext dbContext)
    {
        _teamSlotRepository = teamSlotRepository;
        _teamSlotQuery = teamSlotQuery;
        _teamSlotCharacterRepository = teamSlotCharacterRepository;
        _playerAvailabilityRepository = playerAvailabilityRepository;
        _periodQuery = periodQuery;
        _characterQuery = characterQuery;
        _bossRepository = bossRepository;
        _jobCategoryRepository = jobCategoryRepository;
        _unitOfWork = unitOfWork;
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<TeamSlot>> GetByBossIdAsync(int bossId)
    {
        var period = await _periodQuery.GetByNowAsync();
        var teamSlotCharacters = await _teamSlotQuery.GetByPeriodAndBossIdAsync(period, bossId);

        var result = teamSlotCharacters
            .GroupBy(r => new { r.SlotDateTime, r.TeamSlotId })
            .Select(g => new TeamSlot
            {
                Id = g.Key.TeamSlotId,
                BossId = bossId,
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

        return result;
    }

    public async Task<IEnumerable<TeamSlot>> GetByDiscordIdAsync(ulong discordId)
    {
        var period = await _periodQuery.GetByNowAsync();
        var teamSlotCharacters = await _teamSlotQuery.GetByPeriodAndDiscordIdAsync(period, discordId);
        
        var result = teamSlotCharacters
            .GroupBy(r => new { r.SlotDateTime, r.TeamSlotId })
            .Select(g => new TeamSlot
            {
                Id = g.Key.TeamSlotId,
                BossId = g.FirstOrDefault()?.BossId ?? 0,
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

        return result;
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

                var teamSlotId = await _teamSlotRepository.CreateAsync(teamSlot);
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
                    // 普通玩家：只能刪除屬於自己的角色
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

    public async Task AutoAssignAsync(Register register)
    {
        var period = await _periodQuery.GetByIdAsync(register.PeriodId);
        if (period == null) return;

        var teamSlots = (await _teamSlotRepository.GetByPeriodIdAsync(register.PeriodId)).ToList();
        var characters = await _characterQuery.GetByDiscordIdAsync(register.DiscordId);
        var player = await _dbContext.Repository<PlayerDbModel>().GetByIdAsync((long)register.DiscordId);

        foreach (var cr in register.CharacterRegisters)
        {
            var character = characters.FirstOrDefault(x => x.Id == cr.CharacterId);
            if (character == null || IsAlreadyAssigned(teamSlots, character.Id)) 
                continue;

            var matchingTeam = await FindMatchingTeam(teamSlots, cr.BossId, register, period);

            if (matchingTeam != null)
            {
                var newMember = new TeamSlotCharacter { TeamSlotId = matchingTeam.Id };
                AssignToSlot(newMember, register, character, cr, player);
                await _teamSlotCharacterRepository.CreateAsync(newMember);
                matchingTeam.Characters.Add(newMember);
            }
            else if (register.Availabilities.Any())
            {
                var newTeam = await CreateNewTeamAsync(register, cr, character, player, period);
                teamSlots.Add(newTeam);
            }
        }

        await MergeTeams(register);
    }
    
    private bool IsAlreadyAssigned(List<TeamSlot> teamSlots, string characterId)
    {
        return teamSlots.Any(ts =>
            ts.Characters.Any(c => c.CharacterId == characterId));
    }
    
    private async Task<TeamSlot?> FindMatchingTeam(
        List<TeamSlot> teamSlots,
        int bossId,
        Register register,
        Period period)
    {
        var bosses = await _bossRepository.GetAllAsync();
        var boss = bosses.ToList().FirstOrDefault(x => x.Id == bossId);
        int requireMembers = boss?.RequireMembers ?? 6;

        return teamSlots
            .Where(ts => ts.BossId == bossId)
            .Where(ts => ts.Characters.Count(c => c.CharacterId != null) < requireMembers)
            .FirstOrDefault(ts =>
            {
                var twTime = ts.SlotDateTime.ToOffset(TimeSpan.FromHours(8));

                int weekday = ToIsoWeekday(twTime.DayOfWeek);
                var time = TimeOnly.FromDateTime(twTime.DateTime);

                return register.Availabilities.Any(a => IsTimeInAvailability(weekday, time, a, period));
            });
    }
    
    private void AssignToSlot(
        TeamSlotCharacter slot,
        Register register,
        Character character,
        CharacterRegister cr,
        PlayerDbModel? player)
    {
        slot.DiscordId = register.DiscordId;
        slot.DiscordName = player?.DiscordName ?? "-";
        slot.CharacterId = character.Id;
        slot.CharacterName = character.Name;
        slot.Job = character.Job;
        slot.AttackPower = character.AttackPower;
        slot.Rounds = cr.Rounds;
        slot.IsManual = false;
    }
    
    
    private async Task<TeamSlot> CreateNewTeamAsync(
        Register register,
        CharacterRegister cr,
        Character character,
        PlayerDbModel? player,
        Period period)
    {
        var targetAvail = GetBestAvailability(register, period);
        var targetDateTime = GetNextSlotDate(targetAvail, period);

        var teamSlot = new TeamSlot
        {
            BossId = cr.BossId,
            // 以台灣時間（UTC+8）建立，然後轉為 UTC 儲存
            SlotDateTime = new DateTimeOffset(targetDateTime, TimeSpan.FromHours(8)).ToOffset(TimeSpan.Zero),
            IsTemporary = false,
            IsPublished = true,
        };

        var teamSlotId = await _teamSlotRepository.CreateAsync(teamSlot);

        await CreateTeamMembers(teamSlotId, cr, character, register, player);

        teamSlot.Id = teamSlotId;
        return teamSlot;
    }
    
    private PlayerAvailability GetBestAvailability(Register register, Period period)
    {
        // 取得週期重置時間 (TPE)
        var resetTime = period.StartDate.ToOffset(TimeSpan.FromHours(8)).TimeOfDay;

        return register.Availabilities
            .OrderBy(a =>
            {
                // 楓之谷週期的排序：週四為 0
                int dayWeight = (a.Weekday + 3) % 7;

                // 如果是週四且時間早於重置時間 (08:00)，將其排序權重加 7，視為本週期的最後
                if (a.Weekday == 4 && a.StartTime.ToTimeSpan() < resetTime)
                {
                    return dayWeight + 7;
                }

                return dayWeight;
            })
            .ThenBy(a => a.StartTime) // 同一天則按時間排序
            .FirstOrDefault();
    }
    
    public DateTime GetNextSlotDatePublic(PlayerAvailability avail, Period period) => GetNextSlotDate(avail, period);

    private DateTime GetNextSlotDate(PlayerAvailability avail, Period period)
    {
        // 確保以台灣時間 (UTC+8) 計算
        // period.StartDate 為週四 00:00 UTC = 08:00 TPE
        var periodStartTpe = period.StartDate.ToOffset(TimeSpan.FromHours(8));
        var startDate = periodStartTpe.Date; // 週四的日期

        int targetDayOfWeek = avail.Weekday;
        var slotTime = avail.StartTime.ToTimeSpan();

        // 楓之谷週期的天數偏移 (週四=0, 週五=1, ..., 週三=6)
        int targetOffset = (targetDayOfWeek + 3) % 7;
        
        var slotDate = startDate.AddDays(targetOffset).Add(slotTime);

        // 如果計算出的日期時間早於週期開始時間 (即週四 00:00~08:00)，理論上該時段應歸於該週
        // 但如果玩家選的是週四 00:00，且重置是 08:00，那這應該算是上一週的最後或是本週的開始？
        // 根據 WeeklyPeriodJob，StartDate 是下一週的開始。
        // 所以 4/2 08:00 是 StartDate，如果玩家選週四 00:00，應該是 4/9 00:00。
        
        if (new DateTimeOffset(slotDate, TimeSpan.FromHours(8)) < period.StartDate)
        {
            slotDate = slotDate.AddDays(7);
        }

        return slotDate;
    }

    private bool IsTimeInAvailability(int teamWeekday, TimeOnly teamTime, PlayerAvailability avail, Period period)
    {
        var resetTime = period.StartDate.ToOffset(TimeSpan.FromHours(8)).TimeOfDay;

        // 判定該時段在該週期內是否有效
        // 如果是週四且早於重置時間，該時段屬於「上一個週期」或「本週期的最後（如果加了7天）」
        // 但在匹配隊伍時，隊伍的時間點本身已經是在 Period 內了。
        
        int Next(int isoWeekday) => isoWeekday == 7 ? 1 : isoWeekday + 1;

        int t = teamTime.Hour * 60 + teamTime.Minute;
        int s = avail.StartTime.Hour * 60 + avail.StartTime.Minute;
        int e = (avail.EndTime.Hour == 0 && avail.EndTime.Minute == 0) ? 24 * 60 : avail.EndTime.Hour * 60 + avail.EndTime.Minute;

        bool wraps = s > e;

        bool isInRange;
        if (!wraps)
        {
            isInRange = teamWeekday == avail.Weekday && t >= s && t < e;
        }
        else
        {
            isInRange = (teamWeekday == avail.Weekday && t >= s) || (teamWeekday == Next(avail.Weekday) && t < e);
        }

        if (!isInRange) return false;

        // 額外檢查：如果該時段是週四重置前，則視為無效（除非該時段已經是被延遞過的，但隊伍時間點通常在 Period 內）
        // 這裡的 teamWeekday/teamTime 來自 ts.SlotDateTime，已經在 Period 內了。
        return true;
    }
    
    private async Task CreateTeamMembers(
        int teamSlotId,
        CharacterRegister cr,
        Character character,
        Register register,
        PlayerDbModel? player)
    {
        var firstMember = new TeamSlotCharacter { TeamSlotId = teamSlotId };
        FillPlayer(firstMember, register, character, cr, player);
        await _teamSlotCharacterRepository.CreateAsync(firstMember);
    }
    
    
    private void FillPlayer(
        TeamSlotCharacter slot,
        Register register,
        Character character,
        CharacterRegister cr,
        PlayerDbModel? player)
    {
        slot.DiscordId = register.DiscordId;
        slot.DiscordName = player?.DiscordName ?? "-";
        slot.CharacterId = character.Id;
        slot.CharacterName = character.Name;
        slot.Job = character.Job;
        slot.AttackPower = character.AttackPower;
        slot.Rounds = cr.Rounds;
    }
    
    private async Task MergeTeams(Register register)
    {
        var bossIds = register.CharacterRegisters
            .Select(x => x.BossId)
            .Distinct();

        foreach (var bossId in bossIds)
        {
            await TryMergeTeamsAsync(bossId, register.PeriodId);
        }
    }

private async Task TryMergeTeamsAsync(int bossId, int periodId)
{
    var incompleteTeams = (await _teamSlotRepository
        .GetIncompleteTeamsAsync(bossId, periodId)).ToList();

    if (incompleteTeams.Count < 2) return;

    var templates = await _bossRepository.GetTemplatesByBossIdAsync(bossId);
    var template = templates.FirstOrDefault();

    var boss = (await _bossRepository.GetAllAsync()).FirstOrDefault(x => x.Id == bossId);
    int requireMembers = boss?.RequireMembers ?? 6;

    var jobCategories = (await _jobCategoryRepository.GetAllAsync())
        .GroupBy(x => x.CategoryName)
        .ToDictionary(g => g.Key, g => g.Select(x => x.JobName).ToHashSet());

    // 🔥 一次撈所有 availability
    var allDiscordIds = incompleteTeams
        .SelectMany(t => t.Characters)
        .Where(c => c.CharacterId != null)
        .Select(c => c.DiscordId)
        .Distinct()
        .ToList();

    var allAvailabilities = await _playerAvailabilityRepository
        .GetByDiscordIdsAndPeriodIdAsync(allDiscordIds, periodId);

    var playerAvailabilities = allAvailabilities
        .GroupBy(x => x.DiscordId)
        .ToDictionary(g => g.Key, g => g.AsEnumerable());

    bool merged;

    do
    {
        merged = false;

        for (int i = 0; i < incompleteTeams.Count && !merged; i++)
        {
            for (int j = i + 1; j < incompleteTeams.Count; j++)
            {
                var teamA = incompleteTeams[i];
                var teamB = incompleteTeams[j];

                var membersA = teamA.Characters.Where(c => c.CharacterId != null).ToList();
                var membersB = teamB.Characters.Where(c => c.CharacterId != null).ToList();

                if (membersA.Count + membersB.Count > requireMembers)
                    continue;

                var allMembers = membersA.Concat(membersB).ToList();

                // ❗避免同玩家重複
                if (allMembers.GroupBy(x => x.DiscordId).Any(g => g.Count() > 1))
                    continue;

                // ❗避免動到手動隊伍
                if (teamA.Characters.Any(c => c.IsManual) ||
                    teamB.Characters.Any(c => c.IsManual))
                    continue;

                var period = await _periodQuery.GetByIdAsync(periodId);
                if (period == null) continue;
                var commonTime = await FindCommonDateTime(allMembers, playerAvailabilities, period);
                if (commonTime == null)
                    continue;

                if (template != null)
                {
                    var mergedMembers = TryMatchTemplate(allMembers, template, jobCategories, requireMembers);
                    if (mergedMembers == null)
                        continue;

                    await PerformMerge(teamA, teamB, mergedMembers, commonTime.Value);
                }
                else
                {
                    await PerformMerge(teamA, teamB, null, commonTime.Value);
                }

                incompleteTeams.RemoveAt(j);
                merged = true;
                break;
            }
        }

    } while (merged);
}

    private async Task<DateTimeOffset?> FindCommonDateTime(
        List<TeamSlotCharacter> members, 
        Dictionary<ulong, IEnumerable<PlayerAvailability>> availabilities,
        Period period)
    {
        var memberAvails = new List<IEnumerable<PlayerAvailability>>();
        foreach (var m in members)
        {
            if (availabilities.TryGetValue(m.DiscordId, out var avails))
            {
                memberAvails.Add(avails);
            }
            else
            {
                // 如果有人沒報名時段，就無法找到共同時段（或者應該視為他不限時段？
                // 根據目前邏輯，沒報名時段的人不應該被自動排入，但在 TryMergeTeamsAsync 中
                // 我們是從現有的隊伍（可能是手動調整過）中抓人。
                // 如果找不到報名資料，保險起見回傳 null 表示無法合併。
                return null;
            }
        }
        
        // 楓之谷週期排序：週四(4), 週五(5), 週六(6), 週日(0), 週一(1), 週二(2), 週三(3)
        var weekdays = new[] { 4, 5, 6, 0, 1, 2, 3 };
        
        foreach (var day in weekdays)
        {
            var availsOnDay = memberAvails.Select(list => list.Where(a => a.Weekday == day).ToList()).ToList();
            if (availsOnDay.Any(list => !list.Any())) continue;

            // 尋找所有成員在該天的共同交集時段
            var commonIntervals = availsOnDay[0].Select(a => (a.StartTime, a.EndTime)).ToList();

            for (int i = 1; i < availsOnDay.Count; i++)
            {
                var nextPersonAvails = availsOnDay[i];
                var newIntersections = new List<(TimeOnly StartTime, TimeOnly EndTime)>();

                foreach (var common in commonIntervals)
                {
                    foreach (var personAvail in nextPersonAvails)
                    {
                        // 計算交集
                        var maxStart = common.StartTime > personAvail.StartTime ? common.StartTime : personAvail.StartTime;
                        var minEnd = common.EndTime < personAvail.EndTime ? common.EndTime : personAvail.EndTime;

                        if (maxStart < minEnd)
                        {
                            newIntersections.Add((maxStart, minEnd));
                        }
                    }
                }
                commonIntervals = newIntersections;
                if (!commonIntervals.Any()) break;
            }

            if (commonIntervals.Any())
            {
                // 取最早的交集起始點
                var earliestStartTime = commonIntervals.OrderBy(x => x.StartTime).First().StartTime;
                
                var dummyAvail = new PlayerAvailability { Weekday = day, StartTime = earliestStartTime };
                var targetDateTime = GetNextSlotDate(dummyAvail, period);
                
                return new DateTimeOffset(targetDateTime, TimeSpan.FromHours(8));
            }
        }

        return null;
    }

    public List<TeamSlotCharacter>? TryMatchTemplate(List<TeamSlotCharacter> members, BossTemplate template, Dictionary<string, HashSet<string>> jobCategories, int requireMembers)
    {
        var result = new List<TeamSlotCharacter>();
        var remainingMembers = members.OrderByDescending(m => m.AttackPower).ToList();

        foreach (var req in template.Requirements.OrderBy(r => r.Priority))
        {
            for (int i = 0; i < req.Count; i++)
            {
                var match = remainingMembers.FirstOrDefault(m => IsInJobCategory(m.Job, req.JobCategory, jobCategories));
                if (match != null)
                {
                    result.Add(match);
                    remainingMembers.Remove(match);
                }
                else
                {
                    // 建立空位
                    result.Add(new TeamSlotCharacter
                    {
                        IsManual = false,
                        DiscordName = "-",
                        Job = req.JobCategory
                    });
                }
            }
        }

        // 如果還有剩下的成員沒被放入範本職位
        if (remainingMembers.Any())
        {
            // 檢查總人數是否超過 requireMembers
            if (result.Count + remainingMembers.Count > requireMembers) return null;
            result.AddRange(remainingMembers);
        }

        return result;
    }

    private async Task PerformMerge(TeamSlot teamA, TeamSlot teamB, List<TeamSlotCharacter>? mergedCharacters, DateTimeOffset newDateTime)
    {
        teamA.SlotDateTime = newDateTime;
        if (mergedCharacters != null)
        {
            teamA.Characters = mergedCharacters;
            foreach (var c in teamA.Characters) c.TeamSlotId = teamA.Id;
        }
        else
        {
            // 無範本合併，直接把 B 的成員放入 A 的空位或新增
            var membersB = teamB.Characters.Where(c => c.CharacterId != null).ToList();
            foreach (var mb in membersB)
            {
                var emptySlot = teamA.Characters.FirstOrDefault(c => c.CharacterId == null);
                if (emptySlot != null)
                {
                    emptySlot.DiscordId = mb.DiscordId;
                    emptySlot.DiscordName = mb.DiscordName;
                    emptySlot.CharacterId = mb.CharacterId;
                    emptySlot.CharacterName = mb.CharacterName;
                    emptySlot.Job = mb.Job;
                    emptySlot.AttackPower = mb.AttackPower;
                    emptySlot.Rounds = mb.Rounds;
                    emptySlot.IsManual = mb.IsManual;
                }
                else
                {
                    mb.TeamSlotId = teamA.Id;
                    teamA.Characters.Add(mb);
                }
            }
        }

        await _teamSlotRepository.UpdateAsync(teamA);
        await _teamSlotCharacterRepository.DeleteByTeamSlotIdAsync(teamB.Id);
        await _teamSlotRepository.DeleteAsync(teamB.Id);
    }

    public bool IsInJobCategory(string job, string category, Dictionary<string, HashSet<string>> jobCategories)
    {
        if (category == "任意") return true;
        if (jobCategories.TryGetValue(category, out var jobs))
        {
            return jobs.Contains(job);
        }

        return job == category;
    }
    
    private int ToIsoWeekday(DayOfWeek day)
    {
        return day == DayOfWeek.Sunday ? 7 : (int)day;
    }
}