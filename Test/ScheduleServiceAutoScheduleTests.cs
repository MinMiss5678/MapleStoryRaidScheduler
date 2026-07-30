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
    private readonly Mock<IJobCategoryRepository> _jobCategoryRepositoryMock;
    private readonly Mock<ITeamSlotRepository> _teamSlotRepositoryMock;

    public ScheduleServiceAutoScheduleTests()
    {
        _periodQueryMock = new Mock<IPeriodQuery>();
        _playerRegisterQueryMock = new Mock<IPlayerRegisterQuery>();
        _bossRepositoryMock = new Mock<IBossRepository>();
        _jobCategoryRepositoryMock = new Mock<IJobCategoryRepository>();
        _teamSlotRepositoryMock = new Mock<ITeamSlotRepository>();

        // 預設：無現有隊伍（純重排，無保留隊）
        _teamSlotRepositoryMock.Setup(r => r.GetByPeriodIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<TeamSlot>());
        _teamSlotRepositoryMock.Setup(r => r.GetTemporaryByPeriodIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<TeamSlot>());

        _scheduleService = new ScheduleService(
            _periodQueryMock.Object,
            _playerRegisterQueryMock.Object,
            _bossRepositoryMock.Object,
            _jobCategoryRepositoryMock.Object,
            _teamSlotRepositoryMock.Object);
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
        Name = "",
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
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId)).ReturnsAsync(new Boss { RoundConsumption = 1, Name = "" });
        _periodQueryMock.Setup(q => q.GetActivePeriodAsync()).ReturnsAsync(period);
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
        Assert.Equal(TeamSlotSource.Admin, result[0].Source);
        Assert.True(result[0].Id <= 0); // 新隊以負 Id 標記，存檔時走 CREATE
    }

    [Fact]
    public async Task AutoScheduleWithTemplateAsync_ShouldPreserveProtectedTeam_AndAutoFillEmptySlot()
    {
        // Arrange: 一支含 IsManual 成員的 Admin 保留隊（缺 1 人），重排應保留整隊並補滿空位
        int bossId = 1, templateId = 10;
        var template = CreateTemplate(bossId, templateId); // 需要 2 個「任意」
        var period = CreatePeriod();

        _bossRepositoryMock.Setup(r => r.GetTemplateByIdAsync(templateId)).ReturnsAsync(template);
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId))
            .ReturnsAsync(new Boss { RoundConsumption = 1, RequireMembers = 6, Name = "" });
        _periodQueryMock.Setup(q => q.GetActivePeriodAsync()).ReturnsAsync(period);
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(CreateJobCategories());

        // 保留隊：週四 20:00，已有手動成員 c1(Hero)，缺 1 人
        var protectedTeam = new TeamSlot
        {
            Id = 100,
            BossId = bossId,
            TemplateId = templateId,
            SlotDateTime = new DateTimeOffset(2026, 4, 2, 20, 0, 0, TimeSpan.FromHours(8)),
            Source = TeamSlotSource.Admin,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { Id = 1, TeamSlotId = 100, DiscordId = 11111, DiscordName = "", CharacterId = "c1", CharacterName = "Hero", Job = "Hero", Rounds = 1, IsManual = true }
            }
        };
        _teamSlotRepositoryMock.Setup(r => r.GetTemporaryByPeriodIdAsync(period.Id))
            .ReturnsAsync(new List<TeamSlot> { protectedTeam });
        // 無其他 auto 隊
        _teamSlotRepositoryMock.Setup(r => r.GetByPeriodIdAsync(period.Id))
            .ReturnsAsync(new List<TeamSlot>());

        // 池：c1（已在保留隊）、c2（可補位的 Bishop），皆週四 20:00 可用、各 1 場
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

        // Assert：保留隊仍在（正 Id）、補滿到 2 人
        var kept = result.Single(t => t.Id == 100);
        Assert.Equal(2, kept.Characters.Count(c => c.CharacterId != null));

        var original = kept.Characters.Single(c => c.CharacterId == "c1");
        Assert.True(original.IsManual);          // 原手動成員不動

        var filled = kept.Characters.Single(c => c.CharacterId == "c2");
        Assert.False(filled.IsManual);           // 重排補入者為自動

        // c1、c2 各消耗 1 場後池已空 → 不會再另開新隊
        Assert.DoesNotContain(result, t => t.Id <= 0);
    }

    [Fact]
    public async Task AutoScheduleWithTemplateAsync_ShouldDeductRounds_NotAddRounds_ForProtectedTeamMembers()
    {
        // 保留隊既有成員扣場數用 `-=`；驗證扣完後剩餘場數真的不夠，該玩家不會在其他天被重複排入
        // （若誤植成 `+=`，場數會越用越多，同一玩家可能超出實際報名場數被排更多團）
        int bossId = 1, templateId = 10;
        var template = new BossTemplate
        {
            Id = templateId,
            BossId = bossId,
            Name = "",
            Requirements = new List<BossTemplateRequirement> { new BossTemplateRequirement { JobCategory = "任意", Count = 1, Priority = 1 } }
        };
        var period = CreatePeriod();

        _bossRepositoryMock.Setup(r => r.GetTemplateByIdAsync(templateId)).ReturnsAsync(template);
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId))
            .ReturnsAsync(new Boss { RoundConsumption = 1, RequireMembers = 1, Name = "" });
        _periodQueryMock.Setup(q => q.GetActivePeriodAsync()).ReturnsAsync(period);
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(CreateJobCategories());

        // 保留隊：週四 20:00，已滿（RequireMembers=1、manual 成員 c1 剛好 1 人）
        var protectedTeam = new TeamSlot
        {
            Id = 100,
            BossId = bossId,
            TemplateId = templateId,
            SlotDateTime = new DateTimeOffset(2026, 4, 2, 20, 0, 0, TimeSpan.FromHours(8)),
            Source = TeamSlotSource.Admin,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { Id = 1, TeamSlotId = 100, DiscordId = 11111, DiscordName = "", CharacterId = "c1", CharacterName = "Hero", Job = "Hero", Rounds = 1, IsManual = true }
            }
        };
        _teamSlotRepositoryMock.Setup(r => r.GetTemporaryByPeriodIdAsync(period.Id))
            .ReturnsAsync(new List<TeamSlot> { protectedTeam });
        _teamSlotRepositoryMock.Setup(r => r.GetByPeriodIdAsync(period.Id))
            .ReturnsAsync(new List<TeamSlot>());

        // c1 只報名了 1 場（週四已被保留隊用掉），週五同樣有空但不該再被排——場數應該只剩 0
        var registrations = new List<PlayerRegisterSchedule>
        {
            new PlayerRegisterSchedule
            {
                Id = 1, DiscordId = 11111, DiscordName = "P1", CharacterId = "c1",
                CharacterName = "Hero", Job = "Hero", AttackPower = 1000, Rounds = 1,
                Availabilities = new List<PlayerAvailability>
                {
                    new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) },
                    new PlayerAvailability { Weekday = 5, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) }
                }
            }
        };
        _playerRegisterQueryMock.Setup(q => q.GetByNowPeriodIdAsync(bossId)).ReturnsAsync(registrations);

        // Act
        var result = (await _scheduleService.AutoScheduleWithTemplateAsync(bossId, templateId)).ToList();

        // Assert：週五不該再開一團給 c1（場數已被保留隊扣完，剩 0 < roundConsumption）
        Assert.DoesNotContain(result, t => t.Id <= 0);
    }

    [Fact]
    public async Task AutoScheduleWithTemplateAsync_ShouldNotDoubleBookPoolCandidate_AcrossTwoProtectedTeamsSameDay()
    {
        // 兩支保留隊同一天各缺 1 人，池裡只有 1 個符合條件的候補（cZ）——只能補進其中一隊，
        // 不該同時出現在兩隊（scheduledPlayersByDay 若被錯誤清空會導致這個候補被重複塞進第二隊）
        int bossId = 1, templateId = 10;
        var template = CreateTemplate(bossId, templateId); // 需要 2 個「任意」
        var period = CreatePeriod();

        _bossRepositoryMock.Setup(r => r.GetTemplateByIdAsync(templateId)).ReturnsAsync(template);
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId))
            .ReturnsAsync(new Boss { RoundConsumption = 1, RequireMembers = 2, Name = "" });
        _periodQueryMock.Setup(q => q.GetActivePeriodAsync()).ReturnsAsync(period);
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(CreateJobCategories());

        var slotDateTime = new DateTimeOffset(2026, 4, 2, 20, 0, 0, TimeSpan.FromHours(8)); // 週四 20:00

        var teamX = new TeamSlot
        {
            Id = 100, BossId = bossId, TemplateId = templateId, SlotDateTime = slotDateTime, Source = TeamSlotSource.Admin,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { Id = 1, TeamSlotId = 100, DiscordId = 11111, DiscordName = "", CharacterId = "cX", CharacterName = "Hero", Job = "Hero", Rounds = 1, IsManual = true }
            }
        };
        var teamY = new TeamSlot
        {
            Id = 200, BossId = bossId, TemplateId = templateId, SlotDateTime = slotDateTime, Source = TeamSlotSource.Admin,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { Id = 2, TeamSlotId = 200, DiscordId = 22222, DiscordName = "", CharacterId = "cY", CharacterName = "Hero", Job = "Hero", Rounds = 1, IsManual = true }
            }
        };
        _teamSlotRepositoryMock.Setup(r => r.GetTemporaryByPeriodIdAsync(period.Id))
            .ReturnsAsync(new List<TeamSlot> { teamX, teamY });
        _teamSlotRepositoryMock.Setup(r => r.GetByPeriodIdAsync(period.Id))
            .ReturnsAsync(new List<TeamSlot>());

        var registrations = new List<PlayerRegisterSchedule>
        {
            new PlayerRegisterSchedule
            {
                Id = 1, DiscordId = 11111, DiscordName = "PX", CharacterId = "cX", CharacterName = "Hero", Job = "Hero", AttackPower = 1000, Rounds = 1,
                Availabilities = new List<PlayerAvailability> { new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) } }
            },
            new PlayerRegisterSchedule
            {
                Id = 2, DiscordId = 22222, DiscordName = "PY", CharacterId = "cY", CharacterName = "Hero", Job = "Hero", AttackPower = 1000, Rounds = 1,
                Availabilities = new List<PlayerAvailability> { new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) } }
            },
            new PlayerRegisterSchedule
            {
                Id = 3, DiscordId = 33333, DiscordName = "PZ", CharacterId = "cZ", CharacterName = "Bishop", Job = "Bishop", AttackPower = 900, Rounds = 2,
                Availabilities = new List<PlayerAvailability> { new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) } }
            }
        };
        _playerRegisterQueryMock.Setup(q => q.GetByNowPeriodIdAsync(bossId)).ReturnsAsync(registrations);

        // Act
        var result = (await _scheduleService.AutoScheduleWithTemplateAsync(bossId, templateId)).ToList();

        // Assert：cZ 只能被補進 teamX 或 teamY 其中一隊，不能同時出現在兩隊
        var resultTeamX = result.Single(t => t.Id == 100);
        var resultTeamY = result.Single(t => t.Id == 200);
        int czAppearances = resultTeamX.Characters.Count(c => c.CharacterId == "cZ") + resultTeamY.Characters.Count(c => c.CharacterId == "cZ");
        Assert.Equal(1, czAppearances);
    }

    [Fact]
    public async Task AutoScheduleWithTemplateAsync_ShouldNotFormTeam_WhenInsufficientPlayers()
    {
        // Arrange
        int bossId = 1, templateId = 10;
        var template = new BossTemplate
        {
            Id = templateId,
            Name = "",
            Requirements = new List<BossTemplateRequirement>
            {
                // Requires 3 members but only 2 available
                new BossTemplateRequirement { JobCategory = "任意", Count = 3, Priority = 1, IsOptional = false }
            }
        };
        var period = CreatePeriod();
        var jobCategories = CreateJobCategories();

        _bossRepositoryMock.Setup(r => r.GetTemplateByIdAsync(templateId)).ReturnsAsync(template);
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId)).ReturnsAsync(new Boss { RoundConsumption = 1, Name = "" });
        _periodQueryMock.Setup(q => q.GetActivePeriodAsync()).ReturnsAsync(period);
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(jobCategories);

        var registrations = new List<PlayerRegisterSchedule>
        {
            new PlayerRegisterSchedule
            {
                Id = 1, DiscordId = 11111, DiscordName = "", CharacterId = "", CharacterName = "", Job = "Hero", Rounds = 1,
                Availabilities = new List<PlayerAvailability>
                {
                    new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) }
                }
            },
            new PlayerRegisterSchedule
            {
                Id = 2, DiscordId = 22222, DiscordName = "", CharacterId = "", CharacterName = "", Job = "Bishop", Rounds = 1,
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
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId)).ReturnsAsync(new Boss { RoundConsumption = 1, Name = "" });
        _periodQueryMock.Setup(q => q.GetActivePeriodAsync()).ReturnsAsync(period);
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(jobCategories);

        // 4 players, 2 rounds each → can form 2 teams
        var avail = new List<PlayerAvailability>
        {
            new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) }
        };
        var registrations = new List<PlayerRegisterSchedule>
        {
            new PlayerRegisterSchedule { Id = 1, DiscordId = 1, DiscordName = "", CharacterId = "", CharacterName = "", Job = "Hero", Rounds = 2, Availabilities = avail },
            new PlayerRegisterSchedule { Id = 2, DiscordId = 2, DiscordName = "", CharacterId = "", CharacterName = "", Job = "Bishop", Rounds = 2, Availabilities = avail },
            new PlayerRegisterSchedule { Id = 3, DiscordId = 3, DiscordName = "", CharacterId = "", CharacterName = "", Job = "Bowmaster", Rounds = 2, Availabilities = avail },
            new PlayerRegisterSchedule { Id = 4, DiscordId = 4, DiscordName = "", CharacterId = "", CharacterName = "", Job = "Hero", Rounds = 2, Availabilities = avail },
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
        _bossRepositoryMock.Setup(r => r.GetByIdAsync(bossId)).ReturnsAsync(new Boss { RoundConsumption = 2, Name = "" });
        _periodQueryMock.Setup(q => q.GetActivePeriodAsync()).ReturnsAsync(period);
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(jobCategories);

        var avail = new List<PlayerAvailability>
        {
            new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) }
        };
        var registrations = new List<PlayerRegisterSchedule>
        {
            // Has only 1 round but boss requires 2 → should be skipped
            new PlayerRegisterSchedule { Id = 1, DiscordId = 1, DiscordName = "", CharacterId = "", CharacterName = "", Job = "Hero", Rounds = 1, Availabilities = avail },
            new PlayerRegisterSchedule { Id = 2, DiscordId = 2, DiscordName = "", CharacterId = "", CharacterName = "", Job = "Bishop", Rounds = 2, Availabilities = avail },
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
        _periodQueryMock.Setup(q => q.GetActivePeriodAsync()).ReturnsAsync(period);
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(jobCategories);
        _playerRegisterQueryMock.Setup(q => q.GetByNowPeriodIdAsync(bossId))
            .ReturnsAsync(new List<PlayerRegisterSchedule>());

        // Act & Assert (should not throw, just return empty)
        var result = await _scheduleService.AutoScheduleWithTemplateAsync(bossId, templateId);
        Assert.Empty(result);
    }
}
