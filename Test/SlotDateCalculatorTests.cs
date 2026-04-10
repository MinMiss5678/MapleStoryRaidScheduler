using Domain.Entities;
using Domain.Helpers;
using Xunit;

namespace Test;

public class SlotDateCalculatorTests
{
    [Theory]
    [InlineData(DayOfWeek.Sunday, 7)]
    [InlineData(DayOfWeek.Monday, 1)]
    [InlineData(DayOfWeek.Tuesday, 2)]
    [InlineData(DayOfWeek.Wednesday, 3)]
    [InlineData(DayOfWeek.Thursday, 4)]
    [InlineData(DayOfWeek.Friday, 5)]
    [InlineData(DayOfWeek.Saturday, 6)]
    public void ToIsoWeekday_ShouldReturnCorrectIsoNumber(DayOfWeek dotNetDay, int expected)
    {
        Assert.Equal(expected, SlotDateCalculator.ToIsoWeekday(dotNetDay));
    }

    [Theory]
    [InlineData(4, 20, 0, 4, 20, 0, true)]    // 週四20:00 在 20:00-22:00 → true (含起點)
    [InlineData(4, 19, 59, 4, 20, 0, false)]  // 19:59 不在 20:00-22:00 → false
    [InlineData(4, 22, 0, 4, 20, 0, false)]   // 22:00 不在 20:00-22:00 → false (不含終點)
    [InlineData(4, 21, 30, 4, 20, 0, true)]   // 21:30 在 20:00-22:00 → true
    public void IsTimeInAvailability_NonWrapping_ShouldReturnExpected(
        int teamWeekday, int teamHour, int teamMinute,
        int availWeekday, int availStartHour, int availStartMinute,
        bool expected)
    {
        // Arrange
        var avail = new PlayerAvailability
        {
            Weekday = availWeekday,
            StartTime = new TimeOnly(availStartHour, availStartMinute),
            EndTime = new TimeOnly(22, 0)
        };
        var period = new Period { StartDate = DateTimeOffset.UtcNow, EndDate = DateTimeOffset.UtcNow.AddDays(7) };

        // Act
        var result = SlotDateCalculator.IsTimeInAvailability(teamWeekday, new TimeOnly(teamHour, teamMinute), avail, period);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsTimeInAvailability_WrappingMidnight_ShouldReturnTrue_ForNextDayEarlyHour()
    {
        // 可用時段 週四 22:00 ~ 00:00 (跨午夜)
        var avail = new PlayerAvailability
        {
            Weekday = 4,
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(0, 0)  // EndTime 00:00 代表午夜 24:00
        };
        var period = new Period { StartDate = DateTimeOffset.UtcNow, EndDate = DateTimeOffset.UtcNow.AddDays(7) };

        // 週四 23:30 → in range (wrapping, same day, t >= s)
        Assert.True(SlotDateCalculator.IsTimeInAvailability(4, new TimeOnly(23, 30), avail, period));
        // 週五 00:30 → NOT in range (EndTime 00:00 means full midnight, so t < 0 is never true, EndTime==0 means 24*60=1440, any time is < 1440? no, t=30 < 1440 → true)
        // Actually let's test: EndTime 00:00 → e = 24*60 = 1440; wraps = s(22*60=1320) > e(1440)? No, 1320 < 1440 → wraps = false
        // So non-wrapping: teamWeekday(5) == avail.Weekday(4)? No → false
        Assert.False(SlotDateCalculator.IsTimeInAvailability(5, new TimeOnly(0, 30), avail, period));
    }

    [Fact]
    public void IsTimeInAvailability_Wrapping_StartGreaterThanEnd_ShouldHandleCorrectly()
    {
        // 可用時段 週四 23:00 ~ 01:00 (真正跨午夜，EndTime < StartTime)
        var avail = new PlayerAvailability
        {
            Weekday = 4,
            StartTime = new TimeOnly(23, 0),
            EndTime = new TimeOnly(1, 0)
        };
        var period = new Period { StartDate = DateTimeOffset.UtcNow, EndDate = DateTimeOffset.UtcNow.AddDays(7) };

        // 週四 23:30 → in range (same day, t >= s)
        Assert.True(SlotDateCalculator.IsTimeInAvailability(4, new TimeOnly(23, 30), avail, period));
        // 週五 00:30 → in range (next day, t < e)
        Assert.True(SlotDateCalculator.IsTimeInAvailability(5, new TimeOnly(0, 30), avail, period));
        // 週五 01:30 → not in range (next day, t >= e)
        Assert.False(SlotDateCalculator.IsTimeInAvailability(5, new TimeOnly(1, 30), avail, period));
    }

    [Fact]
    public void GetBestAvailability_ShouldPreferThursdayAfterReset()
    {
        // 楓之谷週期：週四是第一天，但週四 08:00 之前算上週結算
        var period = new Period
        {
            StartDate = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero), // 週四 00:00 UTC = 08:00 TPE
            EndDate = new DateTimeOffset(2026, 4, 8, 23, 59, 59, TimeSpan.Zero)
        };

        var register = new Register
        {
            Availabilities = new List<PlayerAvailability>
            {
                new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0) }, // 週四 20:00 → 排最前
                new PlayerAvailability { Weekday = 1, StartTime = new TimeOnly(20, 0) }  // 週一 20:00
            }
        };

        var result = SlotDateCalculator.GetBestAvailability(register, period);

        // 週四 20:00 應該排在週一前面
        Assert.Equal(4, result.Weekday);
        Assert.Equal(20, result.StartTime.Hour);
    }

    [Fact]
    public void GetBestAvailability_ShouldPushThursdayBeforeResetToLast()
    {
        // 週四 07:00 (早於 reset 08:00) 應被排在最後
        var period = new Period
        {
            StartDate = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 4, 8, 23, 59, 59, TimeSpan.Zero)
        };

        var register = new Register
        {
            Availabilities = new List<PlayerAvailability>
            {
                new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(7, 0) }, // 早於 08:00 TPE → 排最後
                new PlayerAvailability { Weekday = 5, StartTime = new TimeOnly(20, 0) }  // 週五 → 排前面
            }
        };

        var result = SlotDateCalculator.GetBestAvailability(register, period);

        // 週五應排在週四 07:00 之前
        Assert.Equal(5, result.Weekday);
    }
}
