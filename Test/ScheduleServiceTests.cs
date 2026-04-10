using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class ScheduleServiceTests
{
    private readonly ScheduleService _scheduleService;
    private readonly Mock<IPeriodQuery> _periodQueryMock;
    private readonly Mock<IPlayerRegisterQuery> _playerRegisterQueryMock;
    private readonly Mock<IBossRepository> _bossRepositoryMock;
    private readonly Mock<ITeamSlotRepository> _teamSlotRepositoryMock;
    private readonly Mock<IJobCategoryRepository> _jobCategoryRepositoryMock;

    public ScheduleServiceTests()
    {
        _periodQueryMock = new Mock<IPeriodQuery>();
        _playerRegisterQueryMock = new Mock<IPlayerRegisterQuery>();
        _bossRepositoryMock = new Mock<IBossRepository>();
        _teamSlotRepositoryMock = new Mock<ITeamSlotRepository>();
        _jobCategoryRepositoryMock = new Mock<IJobCategoryRepository>();

        _scheduleService = new ScheduleService(
            _periodQueryMock.Object,
            _playerRegisterQueryMock.Object,
            _bossRepositoryMock.Object,
            _teamSlotRepositoryMock.Object,
            _jobCategoryRepositoryMock.Object);
    }

    [Theory]
    // 週期從 2026-04-02 (週四) 00:00 UTC 開始, TPE 08:00 週四
    // startWeekday = (int)DayOfWeek.Thursday = 4
    // weekday=4: offsetDays=0 → 2026-04-02
    // weekday=5: offsetDays=1 → 2026-04-03 (週五)
    // weekday=1: offsetDays=(1-4+7)%7=4 → 2026-04-06 (週一)
    [InlineData(4, 20, 0, 2026, 4, 2, 20, 0)]  // 週四 20:00 → 2026-04-02
    [InlineData(5, 21, 0, 2026, 4, 3, 21, 0)]  // 週五 21:00 → 2026-04-03
    [InlineData(1, 20, 0, 2026, 4, 6, 20, 0)]  // 週一 20:00 → 2026-04-06
    public void GetDateTimeFromPeriod_ShouldReturnCorrectDateTimeOffset(
        int weekday, int hour, int minute,
        int expectedYear, int expectedMonth, int expectedDay,
        int expectedHour, int expectedMinute)
    {
        // Arrange
        var periodStart = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero); // 週四 00:00 UTC = 08:00 TPE
        var periodEnd = new DateTimeOffset(2026, 4, 8, 23, 59, 59, TimeSpan.Zero);
        var startTime = new TimeOnly(hour, minute);

        // Act
        var result = _scheduleService.GetDateTimeFromPeriod(periodStart, periodEnd, weekday, startTime);

        // Assert - 轉換為 TPE (UTC+8) 比較
        var resultTpe = result.ToOffset(TimeSpan.FromHours(8));
        Assert.Equal(expectedYear, resultTpe.Year);
        Assert.Equal(expectedMonth, resultTpe.Month);
        Assert.Equal(expectedDay, resultTpe.Day);
        Assert.Equal(expectedHour, resultTpe.Hour);
        Assert.Equal(expectedMinute, resultTpe.Minute);
    }

    [Fact]
    public void GetDateTimeFromPeriod_ShouldThrow_WhenWeekdayOutOfRange()
    {
        // Arrange - 週期只有一天 (週四)
        var periodStart = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 4, 2, 23, 59, 59, TimeSpan.Zero);

        // Act & Assert - weekday=2 (週二) offsetDays=5 → 2026-04-07, 超過 periodEnd
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _scheduleService.GetDateTimeFromPeriod(periodStart, periodEnd, 2, new TimeOnly(20, 0)));
    }

    [Fact]
    public async Task AutoScheduleWithTemplateAsync_ShouldThrow_WhenTemplateNotFound()
    {
        // Arrange
        _bossRepositoryMock.Setup(r => r.GetTemplateByIdAsync(99)).ReturnsAsync((BossTemplate?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _scheduleService.AutoScheduleWithTemplateAsync(1, 99));
    }

    [Fact]
    public async Task AutoScheduleWithTemplateAsync_ShouldReturnEmpty_WhenNoRegistrations()
    {
        // Arrange
        int bossId = 1, templateId = 10;
        var template = new BossTemplate
        {
            Id = templateId,
            BossId = bossId,
            Requirements = new List<BossTemplateRequirement>
            {
                new BossTemplateRequirement { JobCategory = "任意", Count = 6, Priority = 1 }
            }
        };

        _bossRepositoryMock.Setup(r => r.GetTemplateByIdAsync(templateId)).ReturnsAsync(template);
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId)).ReturnsAsync(new Boss { RoundConsumption = 1 });
        _playerRegisterQueryMock.Setup(q => q.GetByNowPeriodIdAsync(bossId))
            .ReturnsAsync(new List<PlayerRegisterSchedule>());
        _periodQueryMock.Setup(q => q.GetByNowAsync()).ReturnsAsync(new Period
        {
            StartDate = new DateTimeOffset(2026, 4, 3, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 4, 9, 23, 59, 59, TimeSpan.Zero)
        });
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<JobCategory>());

        // Act
        var result = await _scheduleService.AutoScheduleWithTemplateAsync(bossId, templateId);

        // Assert
        Assert.Empty(result);
    }
}
