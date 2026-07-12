using Domain.Entities;
using Xunit;

namespace Test;

public class SystemConfigEntityTests
{
    [Fact]
    public void GetDeadlineForPeriod_ShouldReturnPreviousWeekSameDay_WhenDeadlineWeekdayEqualsPeriodStart()
    {
        // 截止日永遠在週期開始「之前」。週期從週四開始、截止也設週四 → 上一個週四（整整 7 天前）
        var config = new SystemConfig
        {
            DeadlineDayOfWeek = DayOfWeek.Thursday,
            DeadlineTime = new TimeSpan(23, 59, 59)
        };
        var periodStart = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero); // 週四

        var result = config.GetDeadlineForPeriod(periodStart);

        // daysToAdd = (4 - 4 + 7) % 7 - 7 = -7 → 2026-03-26 23:59:59
        Assert.Equal(new DateTime(2026, 3, 26, 23, 59, 59), result.DateTime);
    }

    [Fact]
    public void GetDeadlineForPeriod_ShouldReturnCorrectDay_WhenDeadlineIsWednesday()
    {
        // 截止日在週期開始之前：週期週四開始、截止設週三 → 前一天（週三 04-01）
        var config = new SystemConfig
        {
            DeadlineDayOfWeek = DayOfWeek.Wednesday,
            DeadlineTime = new TimeSpan(23, 59, 59)
        };
        var periodStart = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero); // 週四

        var result = config.GetDeadlineForPeriod(periodStart);

        // (3 - 4 + 7) % 7 - 7 = -1 → 2026-04-01 Wednesday 23:59:59
        Assert.Equal(new DateTime(2026, 4, 1, 23, 59, 59), result.DateTime);
    }

    [Fact]
    public void GetDeadlineForPeriod_ShouldReturnCorrectDay_WhenDeadlineIsMonday()
    {
        // 截止日在週期開始之前：週期週四開始、截止設週一 → 前一個週一（3 天前 03-30）
        var config = new SystemConfig
        {
            DeadlineDayOfWeek = DayOfWeek.Monday,
            DeadlineTime = new TimeSpan(20, 0, 0)
        };
        var periodStart = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero); // 週四

        var result = config.GetDeadlineForPeriod(periodStart);

        // (1 - 4 + 7) % 7 - 7 = -3 → 2026-03-30 Monday 20:00
        Assert.Equal(new DateTime(2026, 3, 30, 20, 0, 0), result.DateTime);
    }
}
