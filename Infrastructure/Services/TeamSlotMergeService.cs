using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Helpers;
using Domain.Repositories;

namespace Infrastructure.Services;

public class TeamSlotMergeService : ITeamSlotMergeService
{
    private readonly ITeamSlotRepository _teamSlotRepository;
    private readonly ITeamSlotCharacterRepository _teamSlotCharacterRepository;
    private readonly IPeriodQuery _periodQuery;
    private readonly IBossRepository _bossRepository;
    private readonly IPlayerAvailabilityRepository _playerAvailabilityRepository;
    private readonly IJobCategoryRepository _jobCategoryRepository;

    public TeamSlotMergeService(
        ITeamSlotRepository teamSlotRepository,
        ITeamSlotCharacterRepository teamSlotCharacterRepository,
        IPeriodQuery periodQuery,
        IBossRepository bossRepository,
        IPlayerAvailabilityRepository playerAvailabilityRepository,
        IJobCategoryRepository jobCategoryRepository)
    {
        _teamSlotRepository = teamSlotRepository;
        _teamSlotCharacterRepository = teamSlotCharacterRepository;
        _periodQuery = periodQuery;
        _bossRepository = bossRepository;
        _playerAvailabilityRepository = playerAvailabilityRepository;
        _jobCategoryRepository = jobCategoryRepository;
    }

    public async Task MergeTeamsAsync(Register register)
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

                    // 手動成員（補位/微調）可參與合併：合併只會把兩隊併成一隊、不會拆散，
                    // 認識的人仍在同隊。範本比對仍嚴格，湊不齊會在下方 TryMatchTemplate 回 null 取消。

                    var period = await _periodQuery.GetByIdAsync(periodId);
                    if (period == null) continue;
                    var commonTime = FindCommonDateTime(allMembers, playerAvailabilities, period);
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

    internal static DateTimeOffset? FindCommonDateTime(
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
                return null;
            }
        }

        // 楓之谷週期排序：重製日優先（見 SlotDateCalculator.ResetDay）。例：週二起 → 2,3,4,5,6,0,1
        var weekdays = SlotDateCalculator.CycleWeekdayOrder();

        foreach (var day in weekdays)
        {
            var availsOnDay = memberAvails.Select(list => list.Where(a => a.Weekday == day).ToList()).ToList();
            if (availsOnDay.Any(list => !list.Any())) continue;

            // 找到所有成員在該天的共同空閒時段
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
                var targetDateTime = SlotDateCalculator.GetNextSlotDate(dummyAvail, period);

                return new DateTimeOffset(targetDateTime, TimeSpan.FromHours(8));
            }
        }

        return null;
    }

    internal static List<TeamSlotCharacter>? TryMatchTemplate(
        List<TeamSlotCharacter> members, BossTemplate template,
        Dictionary<string, HashSet<string>> jobCategories, int requireMembers)
    {
        var result = new List<TeamSlotCharacter>();
        var remainingMembers = members.OrderByDescending(m => m.AttackPower).ToList();

        foreach (var req in template.Requirements.OrderBy(r => r.Priority))
        {
            for (int i = 0; i < req.Count; i++)
            {
                var match = remainingMembers.FirstOrDefault(m =>
                    JobCategoryHelper.IsInJobCategory(m.Job, req.JobCategory, jobCategories));
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

        // 如果還有剩下的成員沒被歸入特定職業
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
            // 無範本合併，直接將 B 的成員搬入 A 的空位或新增
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
}
