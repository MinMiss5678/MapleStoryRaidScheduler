using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Helpers;
using Domain.Repositories;

namespace Infrastructure.Services;

public class ScheduleService : IScheduleService
{
    private readonly IPeriodQuery _periodQuery;
    private readonly IPlayerRegisterQuery _playerRegisterQuery;
    private readonly IBossRepository _bossRepository;
    private readonly IJobCategoryRepository _jobCategoryRepository;
    private readonly ITeamSlotRepository _teamSlotRepository;

    public ScheduleService(
        IPeriodQuery periodQuery,
        IPlayerRegisterQuery playerRegisterQuery,
        IBossRepository bossRepository,
        IJobCategoryRepository jobCategoryRepository,
        ITeamSlotRepository teamSlotRepository)
    {
        _periodQuery = periodQuery;
        _playerRegisterQuery = playerRegisterQuery;
        _bossRepository = bossRepository;
        _jobCategoryRepository = jobCategoryRepository;
        _teamSlotRepository = teamSlotRepository;
    }

    public async Task<IEnumerable<TeamSlot>> AutoScheduleWithTemplateAsync(int bossId, int templateId)
    {
        var template = await _bossRepository.GetTemplateByIdAsync(templateId);
        if (template == null) throw new KeyNotFoundException($"Template {templateId} not found");

        var boss = await _bossRepository.GetByIdAsync(bossId);
        var roundConsumption = boss?.RoundConsumption ?? 1;
        var requireMembers = boss?.RequireMembers ?? 6;

        var characterRegisters = (await _playerRegisterQuery.GetByNowPeriodIdAsync(bossId)).ToList();
        var period = await _periodQuery.GetActivePeriodAsync();
        if (period == null) return [];

        var jobCategories = (await _jobCategoryRepository.GetAllAsync())
            .GroupBy(x => x.CategoryName)
            .ToDictionary(g => g.Key, g => g.Select(x => x.JobName).ToHashSet());

        var schedules = new List<TeamSlot>();
        var scheduledPlayersByDay = new Dictionary<int, HashSet<int>>();

        // === 保留隊：Admin 建立的隊 或 含 IsManual 成員的隊，重排時整隊保留、只自動補滿空位 ===
        var autoTeams = (await _teamSlotRepository.GetByPeriodIdAsync(period.Id))
            .Where(t => t.BossId == bossId).ToList();
        var adminTeams = (await _teamSlotRepository.GetTemporaryByPeriodIdAsync(period.Id))
            .Where(t => t.BossId == bossId).ToList();
        var protectedTeams = adminTeams
            .Concat(autoTeams.Where(t => t.Characters.Any(c => c.IsManual)))
            .ToList();

        // 角色 → 報名資料對照（供扣場數 / 去重）
        var regByCharId = characterRegisters
            .Where(r => r.CharacterId != null)
            .GroupBy(r => r.CharacterId)
            .ToDictionary(g => g.Key, g => g.First());

        // 先扣除保留隊既有成員占用的場數，並標記當日已排（避免重排重複指派同一角色）
        foreach (var pt in protectedTeams)
        {
            var day = TaiwanWeekday(pt.SlotDateTime);
            if (!scheduledPlayersByDay.ContainsKey(day)) scheduledPlayersByDay[day] = new HashSet<int>();
            foreach (var m in pt.Characters.Where(c => c.CharacterId != null))
            {
                if (regByCharId.TryGetValue(m.CharacterId!, out var reg))
                {
                    reg.Rounds -= roundConsumption;
                    scheduledPlayersByDay[day].Add(reg.Id);
                }
            }
        }

        // 再自動補滿保留隊空位（嚴格職業比對；補入者 IsManual=false，之後重排仍可調整）
        foreach (var pt in protectedTeams)
        {
            FillTeamFromPool(pt, template, characterRegisters, jobCategories,
                scheduledPlayersByDay, requireMembers, roundConsumption);
            schedules.Add(pt);
        }

        // 1. 取得所有報名的時段組合 (Day, StartTime)
        var allDaySlots = characterRegisters
            .SelectMany(c => c.Availabilities.Select(a => new { Day = a.Weekday, Slot = a.StartTime }))
            .Distinct()
            .OrderBy(x => x.Day).ThenBy(x => x.Slot)
            .ToList();

        var teamSlotId = 1;

        // 2. 遍歷每個時段嘗試排團（重排剩餘池 → 新隊）

        foreach (var group in allDaySlots)
        {
            if (!scheduledPlayersByDay.ContainsKey(group.Day))
                scheduledPlayersByDay[group.Day] = new HashSet<int>();

            var alreadyScheduled = scheduledPlayersByDay[group.Day];

            // 篩出該時段可用的角色
            var availableChars = characterRegisters
                .Where(c => c.Rounds >= roundConsumption
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
                        var slotDateTime = GetDateTimeFromPeriod(period.StartDate, period.EndDate, group.Day, group.Slot);
                        schedules.Add(new TeamSlot()
                        {
                            // 負 Id 代表尚未存檔的新隊，存檔時走 CREATE（見 TeamSlotService.UpdateAsync）
                            Id = -(teamSlotId++),
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
                            Source = TeamSlotSource.Admin   // 批次重排產生
                        });

                        // 更新剩餘次數與已排團標記
                        foreach (var c in team)
                        {
                            c.Rounds -= roundConsumption;
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

    // 台灣時區的星期（0=週日 .. 6=週六），與 PlayerAvailability.Weekday 慣例一致
    private static int TaiwanWeekday(DateTimeOffset dt)
        => (int)dt.ToOffset(TimeSpan.FromHours(8)).DayOfWeek;

    /// <summary>
    /// 依範本把保留隊的空位用池中符合職業的角色補滿（嚴格比對，湊不齊就留空）。
    /// 補入者標記 IsManual=false（重排自動填），並扣場數、標記當日已排。
    /// </summary>
    private void FillTeamFromPool(
        TeamSlot team,
        BossTemplate template,
        List<PlayerRegisterSchedule> pool,
        Dictionary<string, HashSet<string>> jobCategories,
        Dictionary<int, HashSet<int>> scheduledPlayersByDay,
        int requireMembers,
        int roundConsumption)
    {
        var current = team.Characters.Where(c => c.CharacterId != null).ToList();
        if (current.Count >= requireMembers) return;

        var day = TaiwanWeekday(team.SlotDateTime);
        var twTime = team.SlotDateTime.ToOffset(TimeSpan.FromHours(8));
        var slot = TimeOnly.FromDateTime(twTime.DateTime);
        if (!scheduledPlayersByDay.ContainsKey(day)) scheduledPlayersByDay[day] = new HashSet<int>();
        var scheduledToday = scheduledPlayersByDay[day];

        // 現有成員的職業，用來扣掉已滿足的範本需求
        var remainingJobs = current.Select(c => c.Job).ToList();

        foreach (var req in template.Requirements.OrderBy(r => r.Priority))
        {
            int fulfilled = 0;
            for (int i = remainingJobs.Count - 1; i >= 0 && fulfilled < req.Count; i--)
            {
                if (IsInJobCategory(remainingJobs[i], req.JobCategory, jobCategories))
                {
                    fulfilled++;
                    remainingJobs.RemoveAt(i);
                }
            }

            for (int k = fulfilled; k < req.Count; k++)
            {
                if (current.Count >= requireMembers) return;

                var cand = pool.FirstOrDefault(c =>
                    c.Rounds >= roundConsumption
                    && !scheduledToday.Contains(c.Id)
                    && IsInJobCategory(c.Job, req.JobCategory, jobCategories)
                    && (!req.MinLevel.HasValue || c.Level >= req.MinLevel.Value)
                    && (!req.MinAttribute.HasValue || c.AttackPower >= req.MinAttribute.Value)
                    && c.Availabilities.Any(a => a.Weekday == day && a.StartTime <= slot && a.EndTime > slot));

                if (cand == null) break; // 該職業湊不齊 → 留空（嚴格比對，不放寬）

                var member = new TeamSlotCharacter
                {
                    TeamSlotId = team.Id,
                    DiscordId = cand.DiscordId,
                    DiscordName = cand.DiscordName,
                    CharacterId = cand.CharacterId,
                    CharacterName = cand.CharacterName,
                    Job = cand.Job,
                    AttackPower = cand.AttackPower,
                    Level = cand.Level,
                    Rounds = cand.Rounds,
                    IsManual = false
                };
                team.Characters.Add(member);
                current.Add(member);
                cand.Rounds -= roundConsumption;
                scheduledToday.Add(cand.Id);
            }
        }
    }

    private bool IsInJobCategory(string job, string category, Dictionary<string, HashSet<string>> jobCategories)
        => JobCategoryHelper.IsInJobCategory(job, category, jobCategories);

    public DateTimeOffset GetDateTimeFromPeriod(
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