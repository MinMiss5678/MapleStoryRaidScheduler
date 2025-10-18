using Application.Interface;
using Application.Queries;
using Domain.Entities;

namespace Infrastructure.Services;

public class ScheduleService : IScheduleService
{
    private readonly IPeriodQuery _periodQuery;
    private readonly IPlayerRegisterQuery _playerRegisterQuery;

    public ScheduleService(IPeriodQuery periodQuery, IPlayerRegisterQuery playerRegisterQuery)
    {
        _periodQuery = periodQuery;
        _playerRegisterQuery = playerRegisterQuery;
    }

    public async Task<IEnumerable<TeamSlot>> AutoScheduleAsync(int bossId, int maxTeamSize = 6)
    {
        var characterRegisters = await _playerRegisterQuery.GetByNowPeriodIdAsync(bossId);
        var period = await _periodQuery.GetByNowAsync();
        var schedules = new List<TeamSlot>();

        // 記錄玩家當天是否已經排過團
        var scheduledPlayersByDay = new Dictionary<int, HashSet<int>>(); // key: day, value: playerIds

        // 列出所有報名的 (Day, Timeslot) 組合
        var allDaySlots = characterRegisters
            .SelectMany(c => c.Weekdays.Zip(c.Timeslots, (day, slot) => new { Day = day, Slot = slot }))
            .Distinct()
            .ToList();

        var teamSlotId = 1;

        foreach (var group in allDaySlots)
        {
            if (!scheduledPlayersByDay.ContainsKey(group.Day))
                scheduledPlayersByDay[group.Day] = new HashSet<int>();

            var alreadyScheduled = scheduledPlayersByDay[group.Day];

            // 篩出符合這個 day + slot 的角色
            var availableChars = characterRegisters
                .Where(c => c.Rounds >= 7
                            && c.Weekdays.Contains(group.Day)
                            && c.Timeslots.Contains(group.Slot)
                            && !alreadyScheduled.Contains(c.Id))
                .ToList();
            
            while (availableChars.Any())
            {
                var team = new List<PlayerRegisterSchedule>();
                var usedPlayers = new HashSet<int>();

                // 龍騎
                var dk = availableChars.FirstOrDefault(c =>
                    c.Job == JobCategories.DragonKnight && !usedPlayers.Contains(c.Id));
                if (dk != null)
                {
                    team.Add(dk);
                    usedPlayers.Add(dk.Id);
                }

                // 法師
                var mage = availableChars.FirstOrDefault(c =>
                    JobCategories.Mage.Contains(c.Job) && !usedPlayers.Contains(c.Id));
                if (mage != null)
                {
                    team.Add(mage);
                    usedPlayers.Add(mage.Id);
                }

                // 必要職業不全就跳過
                if (team.Count < 2)
                {
                    availableChars.RemoveAll(c =>
                        c.Job != JobCategories.DragonKnight && !JobCategories.Mage.Contains(c.Job));
                    break;
                }

                // 補其他職業
                foreach (var c in availableChars)
                {
                    if (team.Count >= maxTeamSize) break;
                    if (usedPlayers.Contains(c.Id)) continue;

                    if (c.Job == JobCategories.Thief && team.Any(x => x.Job == JobCategories.Thief)) continue;
                    if (JobCategories.Strength.Contains(c.Job) &&
                        team.Any(x => JobCategories.Strength.Contains(x.Job))) continue;

                    team.Add(c);
                    usedPlayers.Add(c.Id);
                }

                if (team.Count == maxTeamSize)
                {
                    var slotDateTime = await GetDateTimeFromPeriod(period.StartDate, period.EndDate, group.Day, group.Slot);
                    schedules.Add(new TeamSlot()
                    {
                        Id = teamSlotId++,
                        SlotDateTime = slotDateTime,
                        BossId = bossId,
                        Characters = team.Select(x=> new TeamSlotCharacter()
                        { 
                            DiscordId = x.DiscordId,
                            DiscordName = x.DiscordName,
                            CharacterId = x.CharacterId,
                            CharacterName = x.CharacterName,
                            Job = x.Job,
                            AttackPower = x.AttackPower,
                            Rounds = x.Rounds
                        }).ToList(),
                        IsTemporary = true
                    });

                    foreach (var c in team)
                    {
                        c.Rounds -= 7;
                        alreadyScheduled.Add(c.Id); // ✅ 標記這個玩家當天已排團
                    }
                }

                availableChars.RemoveAll(c => usedPlayers.Contains(c.Id));
            }
        }

        return schedules;
    }
    
    public async Task<DateTimeOffset> GetDateTimeFromPeriod(
        DateTimeOffset periodStart, 
        DateTimeOffset periodEnd, 
        int weekday, 
        string timeslot,
        string timeZoneId = "Asia/Taipei")
    {
        // 解析時段 "HH:mm"
        var parts = timeslot.Split(':');
        int hour = int.Parse(parts[0]);
        int minute = parts.Length > 1 ? int.Parse(parts[1]) : 0;

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
            targetDate.Year, targetDate.Month, targetDate.Day, hour, minute, 0, tz.BaseUtcOffset);

        return TimeZoneInfo.ConvertTime(local, tz);
    }
}

public static class JobCategories
{
    public const string DragonKnight = "龍騎";
    public static readonly HashSet<string> Mage = new() { "冰雷", "火毒" };
    public const string Thief = "神偷";
    public const string Priest = "祭司";
    public static readonly HashSet<string> Strength = new() { "十字軍", "騎士", "格鬥家" };
    public static readonly HashSet<string> Agility = new() { "遊俠", "狙擊手", "暗殺者", "神槍手" };
}