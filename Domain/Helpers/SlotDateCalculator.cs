using Domain.Entities;

namespace Domain.Helpers;

public static class SlotDateCalculator
{
    public static PlayerAvailability GetBestAvailability(Register register, Period period)
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

    public static DateTime GetNextSlotDate(PlayerAvailability avail, Period period)
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

        if (new DateTimeOffset(slotDate, TimeSpan.FromHours(8)) < period.StartDate)
        {
            slotDate = slotDate.AddDays(7);
        }

        return slotDate;
    }

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

    public static int ToIsoWeekday(DayOfWeek day)
    {
        return day == DayOfWeek.Sunday ? 7 : (int)day;
    }
}
