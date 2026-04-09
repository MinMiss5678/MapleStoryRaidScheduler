using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Services;

public class ScheduleService : IScheduleService
{
    private readonly IPeriodQuery _periodQuery;
    private readonly IPlayerRegisterQuery _playerRegisterQuery;
    private readonly IBossRepository _bossRepository;
    private readonly ITeamSlotRepository _teamSlotRepository;
    private readonly IJobCategoryRepository _jobCategoryRepository;

    public ScheduleService(
        IPeriodQuery periodQuery, 
        IPlayerRegisterQuery playerRegisterQuery,
        IBossRepository bossRepository,
        ITeamSlotRepository teamSlotRepository,
        IJobCategoryRepository jobCategoryRepository)
    {
        _periodQuery = periodQuery;
        _playerRegisterQuery = playerRegisterQuery;
        _bossRepository = bossRepository;
        _teamSlotRepository = teamSlotRepository;
        _jobCategoryRepository = jobCategoryRepository;
    }

    public async Task<IEnumerable<TeamSlot>> GetPartiesAsync(int periodId)
    {
        // 從資料庫獲取該週期的團隊列表
        return await _teamSlotRepository.GetByPeriodIdAsync(periodId);
    }

    public async Task<bool> JoinTeamAsync(int teamSlotId, int teamSlotCharacterId, string characterId)
    {
        var slot = await _teamSlotRepository.GetByIdAsync(teamSlotId);
        if (slot == null || !slot.IsPublished) return false;

        var characterSlot = slot.Characters.FirstOrDefault(c => c.Id == teamSlotCharacterId);
        if (characterSlot == null || characterSlot.CharacterId != null) return false;

        // 檢查角色是否符合職業需求（Job 可能存的是 JobCategory）
        // 這裡需要串接 CharacterService 獲取角色詳情
        
        characterSlot.CharacterId = characterId;
        characterSlot.IsManual = true;
        
        await _teamSlotRepository.UpdateAsync(slot);
        return true;
    }

    public async Task<bool> FinalizeScheduleAsync(int periodId)
    {
        var temporarySlots = await _teamSlotRepository.GetTemporaryByPeriodIdAsync(periodId);
        foreach (var slot in temporarySlots)
        {
            slot.IsTemporary = false;
            slot.IsPublished = true;
            await _teamSlotRepository.UpdateAsync(slot);
        }
        return true;
    }

    public async Task<IEnumerable<TeamSlot>> AutoScheduleWithTemplateAsync(int bossId, int templateId)
    {
        var template = await _bossRepository.GetTemplateByIdAsync(templateId);
        if (template == null) throw new Exception("Template not found");

        var characterRegisters = await _playerRegisterQuery.GetByNowPeriodIdAsync(bossId);
        var period = await _periodQuery.GetByNowAsync();
        var schedules = new List<TeamSlot>();

        // 1. 取得所有報名的時段組合 (Day, StartTime)
        var allDaySlots = characterRegisters
            .SelectMany(c => c.Availabilities.Select(a => new { Day = a.Weekday, Slot = a.StartTime }))
            .Distinct()
            .OrderBy(x => x.Day).ThenBy(x => x.Slot)
            .ToList();

        var scheduledPlayersByDay = new Dictionary<int, HashSet<int>>();
        var teamSlotId = 1;

        // 2. 遍歷每個時段嘗試排團
        var jobCategories = (await _jobCategoryRepository.GetAllAsync())
            .GroupBy(x => x.CategoryName)
            .ToDictionary(g => g.Key, g => g.Select(x => x.JobName).ToHashSet());

        foreach (var group in allDaySlots)
        {
            if (!scheduledPlayersByDay.ContainsKey(group.Day))
                scheduledPlayersByDay[group.Day] = new HashSet<int>();

            var alreadyScheduled = scheduledPlayersByDay[group.Day];

            // 篩出該時段可用的角色
            var availableChars = characterRegisters
                .Where(c => c.Rounds >= 7
                            && c.Availabilities.Any(a => a.Weekday == group.Day && a.StartTime <= group.Slot && a.EndTime > group.Slot)
                            && !alreadyScheduled.Contains(c.Id))
                .ToList();

            // 按照場數分組
            var charGroupsByRounds = availableChars
                .GroupBy(c => c.Rounds)
                .OrderByDescending(g => g.Key)
                .ToList();

            foreach (var roundGroup in charGroupsByRounds)
            {
                var currentRoundAvailableChars = roundGroup.ToList();
                
                // 持續嘗試從該場數分組的可用角色中組成團隊
                while (true)
                {
                    var team = new List<PlayerRegisterSchedule>();
                    var usedInThisTeam = new HashSet<int>();
                    bool canFormTeam = true;

                    // 3. 依照範本需求優先級填入角色
                    foreach (var req in template.Requirements.OrderBy(r => r.Priority))
                    {
                        int needed = req.Count;
                        int found = 0;

                        // 找出符合職業類別且滿足最低門檻的角色
                        var matchedChars = currentRoundAvailableChars
                            .Where(c => !usedInThisTeam.Contains(c.Id))
                            .Where(c => IsInJobCategory(c.Job, req.JobCategory, jobCategories))
                            .Where(c => !req.MinLevel.HasValue || c.Level >= req.MinLevel.Value)
                            .Where(c => !req.MinAttribute.HasValue || c.AttackPower >= req.MinAttribute.Value)
                            .Take(needed)
                            .ToList();

                        foreach (var mc in matchedChars)
                        {
                            team.Add(mc);
                            usedInThisTeam.Add(mc.Id);
                            found++;
                        }

                        // 如果不是選配且數量不足，則此團隊無法組成
                        if (!req.IsOptional && found < needed)
                        {
                            canFormTeam = false;
                            break;
                        }
                    }

                    if (canFormTeam && team.Any())
                    {
                        var slotDateTime = await GetDateTimeFromPeriod(period.StartDate, period.EndDate, group.Day, group.Slot);
                        schedules.Add(new TeamSlot()
                        {
                            Id = teamSlotId++,
                            SlotDateTime = slotDateTime,
                            BossId = bossId,
                            TemplateId = templateId,
                            Characters = team.Select(x => new TeamSlotCharacter()
                            {
                                DiscordId = x.DiscordId,
                                DiscordName = x.DiscordName,
                                CharacterId = x.CharacterId,
                                CharacterName = x.CharacterName,
                                Job = x.Job,
                                AttackPower = x.AttackPower,
                                Level = x.Level,
                                Rounds = x.Rounds
                            }).ToList(),
                            IsTemporary = true
                        });

                        // 更新剩餘次數與已排團標記
                        foreach (var c in team)
                        {
                            c.Rounds -= 7;
                            alreadyScheduled.Add(c.Id);
                        }
                        
                        // 從當前場數分組列表中移除已使用的角色
                        currentRoundAvailableChars.RemoveAll(c => usedInThisTeam.Contains(c.Id));
                    }
                    else
                    {
                        // 無法再從當前分組組成團隊，跳出 while 迴圈
                        break;
                    }
                }
            }
        }

        return schedules;
    }

    private bool IsInJobCategory(string job, string category, Dictionary<string, HashSet<string>> jobCategories)
    {
        if (string.IsNullOrWhiteSpace(category)) return false;
        
        // 支援多個職業以逗號或斜線分隔
        var categories = category.Split(new[] { ',', '/', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var cat in categories)
        {
            if (job == cat) return true;
            
            // 檢查資料庫定義的集合
            if (jobCategories.TryGetValue(cat, out var jobs) && jobs.Contains(job)) return true;
        }
        
        return false;
    }

    public async Task<DateTimeOffset> GetDateTimeFromPeriod(
        DateTimeOffset periodStart, 
        DateTimeOffset periodEnd, 
        int weekday, 
        TimeOnly startTime,
        string timeZoneId = "Asia/Taipei")
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        // 將星期日 DayOfWeek=0 改成 7
        int startWeekday = (int)periodStart.DayOfWeek;
        if (startWeekday == 0) startWeekday = 7;

        // 計算 offsetDays
        int offsetDays = weekday - startWeekday;
        if (offsetDays < 0)
            offsetDays += 7; // 若週期橫跨下週

        // 套用偏移
        var targetDate = periodStart.Date.AddDays(offsetDays);

        // 驗證是否在週期內
        if (targetDate < periodStart.Date || targetDate > periodEnd.Date)
            throw new ArgumentOutOfRangeException(nameof(weekday), $"Weekday {weekday} 不在週期範圍內");

        var local = new DateTimeOffset(
            targetDate.Year, targetDate.Month, targetDate.Day, startTime.Hour, startTime.Minute, 0, tz.BaseUtcOffset);

        return TimeZoneInfo.ConvertTime(local, tz);
    }
}