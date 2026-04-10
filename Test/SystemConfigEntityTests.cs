using Domain.Entities;
using Xunit;

namespace Test;

public class SystemConfigEntityTests
{
    [Fact]
    public void GetDeadlineForPeriod_ShouldReturnSameDay_WhenDeadlineIsSameDayAsPeriodStart()
    {
        // 週期從週四開始，截止日也是週四 23:59:59
        var config = new SystemConfig
        {
            DeadlineDayOfWeek = DayOfWeek.Thursday,
            DeadlineTime = new TimeSpan(23, 59, 59)
        };
        var periodStart = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero); // 週四

        var result = config.GetDeadlineForPeriod(periodStart);

        // daysToAdd = (4 - 4 + 7) % 7 = 0 → 2026-04-02 23:59:59
        Assert.Equal(new DateTime(2026, 4, 2, 23, 59, 59), result.DateTime);
    }

    [Fact]
    public void GetDeadlineForPeriod_ShouldReturnCorrectDay_WhenDeadlineIsWednesday()
    {
        // 週期從週四開始，截止日是下週三 23:59:59 (6天後)
        var config = new SystemConfig
        {
            DeadlineDayOfWeek = DayOfWeek.Wednesday,
            DeadlineTime = new TimeSpan(23, 59, 59)
        };
        var periodStart = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero); // 週四

        var result = config.GetDeadlineForPeriod(periodStart);

        // (3 - 4 + 7) % 7 = 6 天後 → 2026-04-08 Wednesday
        Assert.Equal(new DateTime(2026, 4, 8, 23, 59, 59), result.DateTime);
    }

    [Fact]
    public void GetDeadlineForPeriod_ShouldReturnCorrectDay_WhenDeadlineIsMonday()
    {
        // 週期從週四開始，截止日是週一 (4天後)
        var config = new SystemConfig
        {
            DeadlineDayOfWeek = DayOfWeek.Monday,
            DeadlineTime = new TimeSpan(20, 0, 0)
        };
        var periodStart = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero); // 週四

        var result = config.GetDeadlineForPeriod(periodStart);

        // (1 - 4 + 7) % 7 = 4 天後 → 2026-04-06 Monday 20:00
        Assert.Equal(new DateTime(2026, 4, 6, 20, 0, 0), result.DateTime);
    }
}
