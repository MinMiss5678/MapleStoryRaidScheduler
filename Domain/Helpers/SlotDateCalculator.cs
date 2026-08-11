using Domain.Entities;

namespace Domain.Helpers;

public static class SlotDateCalculator
{
    /// <summary>
    /// 楓之谷官方每週重製日（＝週期第一天）的單一事實來源。
    /// 官方改期時只改這裡，所有週期排序 / slot 日期 / 背景排程都會跟著推導。
    /// 重製「時間」不在此定義——它由 period.StartDate 的當日時間推導（目前週二 00:00 UTC = 08:00 TPE）。
    /// </summary>
    public const DayOfWeek ResetDay = DayOfWeek.Tuesday;

    /// <summary>週期內的天別偏移：重製日 = 0、隔天 = 1 … 前一天 = 6（以 ResetDay 為基準旋轉）。</summary>
    public static int CycleDayOffset(int weekday) => (weekday - (int)ResetDay + 7) % 7;

    /// <summary>週期內天別排序（重製日優先），供合併 / 顯示使用。例：週二起 → [2,3,4,5,6,0,1]。</summary>
    public static int[] CycleWeekdayOrder()
        => Enumerable.Range(0, 7).Select(i => ((int)ResetDay + i) % 7).ToArray();

    /// <summary>
    /// 從 now 起算下一個重製日的 00:00 UTC。
    /// 若今天正是重製日且已過 00:00，回傳下週的重製日（與 WeeklyPeriodJob / DeadlineJob 共用）。
    /// </summary>
    public static DateTimeOffset NextReset(DateTimeOffset now)
    {
        int days = ((int)ResetDay - (int)now.DayOfWeek + 7) % 7;
        if (days == 0) days = 7;
        return new DateTimeOffset(now.Date.AddDays(days), TimeSpan.Zero);
    }

    public static PlayerAvailability GetBestAvailability(Register register, Period period)
    {
        // 取得週期重置時間 (TPE)
        var resetTime = period.StartDate.ToOffset(TimeSpan.FromHours(8)).TimeOfDay;

        return register.Availabilities
            .OrderBy(a =>
            {
                // 依重製日旋轉：ResetDay 當天為 0
                int dayWeight = CycleDayOffset(a.Weekday);

                // 若為重製日當天、且時間早於重製時間 (08:00)，權重加 7，視為本週期最後（屬上一輪殘留）
                if (a.Weekday == (int)ResetDay && a.StartTime.ToTimeSpan() < resetTime)
                {
                    return dayWeight + 7;
                }

                return dayWeight;
            })
            .ThenBy(a => a.StartTime) // 同一天則按時間排序
            .First(); // 呼叫端保證 Availabilities 非空（AutoAssign 前已檢查 .Any()）
    }

    public static DateTime GetNextSlotDate(PlayerAvailability avail, Period period)
    {
        // 確保以台灣時間 (UTC+8) 計算
        // period.StartDate 為重製日 00:00 UTC = 08:00 TPE
        var periodStartTpe = period.StartDate.ToOffset(TimeSpan.FromHours(8));
        var startDate = periodStartTpe.Date; // 重製日的日期

        int targetDayOfWeek = avail.Weekday;
        var slotTime = avail.StartTime.ToTimeSpan();

        // 週期內天數偏移：重製日=0, 隔天=1, ..., 前一天=6
        int targetOffset = CycleDayOffset(targetDayOfWeek);

        var slotDate = startDate.AddDays(targetOffset).Add(slotTime);

        if (new DateTimeOffset(slotDate, TimeSpan.FromHours(8)) < period.StartDate)
        {
            slotDate = slotDate.AddDays(7);
        }

        return slotDate;
    }

    /// <summary>period-less 重疊判定：純 weekday+time（period 參數本就未用，見 4-arg 多載）。</summary>
    public static bool IsTimeInAvailability(int teamWeekday, TimeOnly teamTime, PlayerAvailability avail)
        => IsTimeInAvailability(teamWeekday, teamTime, avail, null!);

    public static bool IsTimeInAvailability(int teamWeekday, TimeOnly teamTime, PlayerAvailability avail, Period period)
    {
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

        return isInRange;
    }

    /// <summary>
    /// 團時間是否落在某時段窗內（不看星期，供日期 override 用；period-less §8 Phase 2b）。
    /// EndTime 00:00 視為當日 24:00（整天）。不處理跨日 wrap（override 為同日窗）。
    /// </summary>
    public static bool IsTimeInWindow(TimeOnly teamTime, TimeOnly start, TimeOnly end)
    {
        int t = teamTime.Hour * 60 + teamTime.Minute;
        int s = start.Hour * 60 + start.Minute;
        int e = (end.Hour == 0 && end.Minute == 0) ? 24 * 60 : end.Hour * 60 + end.Minute;
        return t >= s && t < e;
    }

    public static int ToIsoWeekday(DayOfWeek day)
    {
        return day == DayOfWeek.Sunday ? 7 : (int)day;
    }
}
