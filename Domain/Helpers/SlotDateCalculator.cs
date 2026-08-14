using Domain.Entities;

namespace Domain.Helpers;

public static class SlotDateCalculator
{
    // period-less（Phase 4d）：重製日/週期天別排序（ResetDay/CycleDayOffset/CycleWeekdayOrder/NextReset）
    // 隨自動排團/週期 job 退場一併移除——period-less 下不再有「週期第一天」概念。

    /// <summary>period-less 重疊判定：純 weekday+time（Period 已退場，Phase 4d）。EndTime 00:00 視為 24:00；支援跨午夜 wrap。</summary>
    public static bool IsTimeInAvailability(int teamWeekday, TimeOnly teamTime, PlayerAvailability avail)
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
