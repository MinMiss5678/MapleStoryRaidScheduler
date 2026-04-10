using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class ScheduleServiceAutoScheduleTests
{
    private readonly ScheduleService _scheduleService;
    private readonly Mock<IPeriodQuery> _periodQueryMock;
    private readonly Mock<IPlayerRegisterQuery> _playerRegisterQueryMock;
    private readonly Mock<IBossRepository> _bossRepositoryMock;
    private readonly Mock<ITeamSlotRepository> _teamSlotRepositoryMock;
    private readonly Mock<IJobCategoryRepository> _jobCategoryRepositoryMock;

    public ScheduleServiceAutoScheduleTests()
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

    private Period CreatePeriod() => new Period
    {
        Id = 1,
        StartDate = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero), // 週四 00:00 UTC
        EndDate = new DateTimeOffset(2026, 4, 8, 23, 59, 59, TimeSpan.Zero)
    };

    private BossTemplate CreateTemplate(int bossId = 1, int templateId = 10) => new BossTemplate
    {
        Id = templateId,
        BossId = bossId,
        Requirements = new List<BossTemplateRequirement>
        {
            new BossTemplateRequirement { JobCategory = "任意", Count = 2, Priority = 1 }
        }
    };

    private List<JobCategory> CreateJobCategories() => new List<JobCategory>
    {
        new JobCategory { CategoryName = "任意", JobName = "Hero" },
        new JobCategory { CategoryName = "任意", JobName = "Bishop" },
        new JobCategory { CategoryName = "任意", JobName = "Bowmaster" }
    };

    [Fact]
    public async Task AutoScheduleWithTemplateAsync_ShouldFormTeam_WhenEnoughRegistrations()
    {
        // Arrange
        int bossId = 1, templateId = 10;
        var template = CreateTemplate(bossId, templateId);
        var period = CreatePeriod();
        var jobCategories = CreateJobCategories();

        _bossRepositoryMock.Setup(r => r.GetTemplateByIdAsync(templateId)).ReturnsAsync(template);
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId)).ReturnsAsync(new Boss { RoundConsumption = 1 });
        _periodQueryMock.Setup(q => q.GetByNowAsync()).ReturnsAsync(period);
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(jobCategories);

        // 2 players both available 週四 20:00-22:00, with 1 round each
        var registrations = new List<PlayerRegisterSchedule>
        {
            new PlayerRegisterSchedule
            {
                Id = 1, DiscordId = 11111, DiscordName = "P1", CharacterId = "c1",
                CharacterName = "Hero", Job = "Hero", AttackPower = 1000, Rounds = 1,
                Availabilities = new List<PlayerAvailability>
                {
                    new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) }
                }
            },
            new PlayerRegisterSchedule
            {
                Id = 2, DiscordId = 22222, DiscordName = "P2", CharacterId = "c2",
                CharacterName = "Bishop", Job = "Bishop", AttackPower = 900, Rounds = 1,
                Availabilities = new List<PlayerAvailability>
                {
                    new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) }
                }
            }
        };
        _playerRegisterQueryMock.Setup(q => q.GetByNowPeriodIdAsync(bossId)).ReturnsAsync(registrations);

        // Act
        var result = (await _scheduleService.AutoScheduleWithTemplateAsync(bossId, templateId)).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(2, result[0].Characters.Count);
        Assert.Equal(bossId, result[0].BossId);
        Assert.Equal(templateId, result[0].TemplateId);
        Assert.True(result[0].IsTemporary);
    }

    [Fact]
    public async Task AutoScheduleWithTemplateAsync_ShouldNotFormTeam_WhenInsufficientPlayers()
    {
        // Arrange
        int bossId = 1, templateId = 10;
        var template = new BossTemplate
        {
            Id = templateId,
            Requirements = new List<BossTemplateRequirement>
            {
                // Requires 3 members but only 2 available
                new BossTemplateRequirement { JobCategory = "任意", Count = 3, Priority = 1, IsOptional = false }
            }
        };
        var period = CreatePeriod();
        var jobCategories = CreateJobCategories();

        _bossRepositoryMock.Setup(r => r.GetTemplateByIdAsync(templateId)).ReturnsAsync(template);
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId)).ReturnsAsync(new Boss { RoundConsumption = 1 });
        _periodQueryMock.Setup(q => q.GetByNowAsync()).ReturnsAsync(period);
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(jobCategories);

        var registrations = new List<PlayerRegisterSchedule>
        {
            new PlayerRegisterSchedule
            {
                Id = 1, DiscordId = 11111, Job = "Hero", Rounds = 1,
                Availabilities = new List<PlayerAvailability>
                {
                    new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) }
                }
            },
            new PlayerRegisterSchedule
            {
                Id = 2, DiscordId = 22222, Job = "Bishop", Rounds = 1,
                Availabilities = new List<PlayerAvailability>
                {
                    new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) }
                }
            }
        };
        _playerRegisterQueryMock.Setup(q => q.GetByNowPeriodIdAsync(bossId)).ReturnsAsync(registrations);

        // Act
        var result = await _scheduleService.AutoScheduleWithTemplateAsync(bossId, templateId);

        // Assert - 2 players can't satisfy requirement of 3
        Assert.Empty(result);
    }

    [Fact]
    public async Task AutoScheduleWithTemplateAsync_ShouldFormMultipleTeams_WhenEnoughRounds()
    {
        // Arrange
        int bossId = 1, templateId = 10;
        var template = CreateTemplate(bossId, templateId);
        var period = CreatePeriod();
        var jobCategories = CreateJobCategories();

        _bossRepositoryMock.Setup(r => r.GetTemplateByIdAsync(templateId)).ReturnsAsync(template);
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId)).ReturnsAsync(new Boss { RoundConsumption = 1 });
        _periodQueryMock.Setup(q => q.GetByNowAsync()).ReturnsAsync(period);
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(jobCategories);

        // 4 players, 2 rounds each → can form 2 teams
        var avail = new List<PlayerAvailability>
        {
            new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) }
        };
        var registrations = new List<PlayerRegisterSchedule>
        {
            new PlayerRegisterSchedule { Id = 1, DiscordId = 1, Job = "Hero", Rounds = 2, Availabilities = avail },
            new PlayerRegisterSchedule { Id = 2, DiscordId = 2, Job = "Bishop", Rounds = 2, Availabilities = avail },
            new PlayerRegisterSchedule { Id = 3, DiscordId = 3, Job = "Bowmaster", Rounds = 2, Availabilities = avail },
            new PlayerRegisterSchedule { Id = 4, DiscordId = 4, Job = "Hero", Rounds = 2, Availabilities = avail },
        };
        _playerRegisterQueryMock.Setup(q => q.GetByNowPeriodIdAsync(bossId)).ReturnsAsync(registrations);

        // Act
        var result = (await _scheduleService.AutoScheduleWithTemplateAsync(bossId, templateId)).ToList();

        // Assert - should form 2 teams (each needing 2 players with 4 available)
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task AutoScheduleWithTemplateAsync_ShouldSkipPlayer_WhenInsufficientRounds()
    {
        // Arrange - boss needs 2 rounds but player only has 1
        int bossId = 1, templateId = 10;
        var template = CreateTemplate(bossId, templateId);
        var period = CreatePeriod();
        var jobCategories = CreateJobCategories();

        _bossRepositoryMock.Setup(r => r.GetTemplateByIdAsync(templateId)).ReturnsAsync(template);
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId)).ReturnsAsync(new Boss { RoundConsumption = 2 });
        _periodQueryMock.Setup(q => q.GetByNowAsync()).ReturnsAsync(period);
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(jobCategories);

        var avail = new List<PlayerAvailability>
        {
            new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) }
        };
        var registrations = new List<PlayerRegisterSchedule>
        {
            // Has only 1 round but boss requires 2 → should be skipped
            new PlayerRegisterSchedule { Id = 1, DiscordId = 1, Job = "Hero", Rounds = 1, Availabilities = avail },
            new PlayerRegisterSchedule { Id = 2, DiscordId = 2, Job = "Bishop", Rounds = 2, Availabilities = avail },
        };
        _playerRegisterQueryMock.Setup(q => q.GetByNowPeriodIdAsync(bossId)).ReturnsAsync(registrations);

        // Act
        var result = await _scheduleService.AutoScheduleWithTemplateAsync(bossId, templateId);

        // Assert - can't form team as only 1 player has enough rounds
        Assert.Empty(result);
    }

    [Fact]
    public async Task AutoScheduleWithTemplateAsync_ShouldHandleBossWithNoRecord()
    {
        // Arrange - GetByIdAsync returns null → fallback RoundConsumption = 1
        int bossId = 1, templateId = 10;
        var template = CreateTemplate(bossId, templateId);
        var period = CreatePeriod();
        var jobCategories = CreateJobCategories();

        _bossRepositoryMock.Setup(r => r.GetTemplateByIdAsync(templateId)).ReturnsAsync(template);
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId)).ReturnsAsync((Boss?)null); // null boss
        _periodQueryMock.Setup(q => q.GetByNowAsync()).ReturnsAsync(period);
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(jobCategories);
        _playerRegisterQueryMock.Setup(q => q.GetByNowPeriodIdAsync(bossId))
            .ReturnsAsync(new List<PlayerRegisterSchedule>());

        // Act & Assert (should not throw, just return empty)
        var result = await _scheduleService.AutoScheduleWithTemplateAsync(bossId, templateId);
        Assert.Empty(result);
    }
}
