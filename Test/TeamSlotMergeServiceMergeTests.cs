using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class TeamSlotMergeServiceMergeTests
{
    private readonly Mock<ITeamSlotRepository> _teamSlotRepositoryMock;
    private readonly Mock<ITeamSlotCharacterRepository> _teamSlotCharacterRepositoryMock;
    private readonly Mock<IPeriodQuery> _periodQueryMock;
    private readonly Mock<IBossRepository> _bossRepositoryMock;
    private readonly Mock<IPlayerAvailabilityRepository> _playerAvailabilityRepositoryMock;
    private readonly Mock<IJobCategoryRepository> _jobCategoryRepositoryMock;
    private readonly TeamSlotMergeService _mergeService;

    public TeamSlotMergeServiceMergeTests()
    {
        _teamSlotRepositoryMock = new Mock<ITeamSlotRepository>();
        _teamSlotCharacterRepositoryMock = new Mock<ITeamSlotCharacterRepository>();
        _periodQueryMock = new Mock<IPeriodQuery>();
        _bossRepositoryMock = new Mock<IBossRepository>();
        _playerAvailabilityRepositoryMock = new Mock<IPlayerAvailabilityRepository>();
        _jobCategoryRepositoryMock = new Mock<IJobCategoryRepository>();

        _mergeService = new TeamSlotMergeService(
            _teamSlotRepositoryMock.Object,
            _teamSlotCharacterRepositoryMock.Object,
            _periodQueryMock.Object,
            _bossRepositoryMock.Object,
            _playerAvailabilityRepositoryMock.Object,
            _jobCategoryRepositoryMock.Object);
    }

    [Fact]
    public async Task MergeTeamsAsync_ShouldDoNothing_WhenRegisterHasNoBosses()
    {
        // Arrange
        var register = new Register
        {
            PeriodId = 1,
            CharacterRegisters = new List<CharacterRegister>()
        };

        // Act (no exception expected)
        await _mergeService.MergeTeamsAsync(register);

        // Assert - no repo calls
        _teamSlotRepositoryMock.Verify(r => r.GetIncompleteTeamsAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task MergeTeamsAsync_ShouldDoNothing_WhenLessThanTwoIncompleteTeams()
    {
        // Arrange
        _teamSlotRepositoryMock
            .Setup(r => r.GetIncompleteTeamsAsync(1, 1))
            .ReturnsAsync(new List<TeamSlot> { new TeamSlot { Id = 10, BossId = 1 } }); // only 1 team

        var register = new Register
        {
            PeriodId = 1,
            CharacterRegisters = new List<CharacterRegister>
            {
                new CharacterRegister { BossId = 1, CharacterId = "c1" }
            }
        };

        // Act
        await _mergeService.MergeTeamsAsync(register);

        // Assert - no merge attempted
        _periodQueryMock.Verify(q => q.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task MergeTeamsAsync_ShouldSkipMerge_WhenTeamsHaveManualMembers()
    {
        // Arrange
        var teamA = new TeamSlot
        {
            Id = 1, BossId = 5,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { CharacterId = "c1", DiscordId = 111, IsManual = true } // manual!
            }
        };
        var teamB = new TeamSlot
        {
            Id = 2, BossId = 5,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { CharacterId = "c2", DiscordId = 222 }
            }
        };

        _teamSlotRepositoryMock.Setup(r => r.GetIncompleteTeamsAsync(5, 1))
            .ReturnsAsync(new List<TeamSlot> { teamA, teamB });
        _bossRepositoryMock.Setup(r => r.GetTemplatesByBossIdAsync(5)).ReturnsAsync(new List<BossTemplate>());
        _bossRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Boss> { new Boss { Id = 5, RequireMembers = 6 } });
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<JobCategory>());
        _playerAvailabilityRepositoryMock.Setup(r =>
            r.GetByDiscordIdsAndPeriodIdAsync(It.IsAny<List<ulong>>(), 1))
            .ReturnsAsync(new List<PlayerAvailability>());

        var register = new Register
        {
            PeriodId = 1,
            CharacterRegisters = new List<CharacterRegister>
            {
                new CharacterRegister { BossId = 5, CharacterId = "c1" }
            }
        };

        // Act
        await _mergeService.MergeTeamsAsync(register);

        // Assert - no merge should happen (team A has manual member)
        _teamSlotRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<TeamSlot>()), Times.Never);
    }

    [Fact]
    public async Task MergeTeamsAsync_ShouldMergeTeams_WhenCommonTimeExists_NoTemplate()
    {
        // Arrange - two teams, no template, have common time, no manual members
        ulong discordId1 = 111, discordId2 = 222;
        var teamA = new TeamSlot
        {
            Id = 1, BossId = 5,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { CharacterId = "c1", DiscordId = discordId1, IsManual = false }
            }
        };
        var teamB = new TeamSlot
        {
            Id = 2, BossId = 5,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { CharacterId = "c2", DiscordId = discordId2, IsManual = false }
            }
        };

        var period = new Period
        {
            Id = 1,
            StartDate = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero), // 週四
            EndDate = new DateTimeOffset(2026, 4, 8, 23, 59, 59, TimeSpan.Zero)
        };

        _teamSlotRepositoryMock.Setup(r => r.GetIncompleteTeamsAsync(5, 1))
            .ReturnsAsync(new List<TeamSlot> { teamA, teamB });
        _bossRepositoryMock.Setup(r => r.GetTemplatesByBossIdAsync(5)).ReturnsAsync(new List<BossTemplate>());
        _bossRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Boss>
        {
            new Boss { Id = 5, RequireMembers = 6 }
        });
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<JobCategory>());
        _periodQueryMock.Setup(q => q.GetByIdAsync(1)).ReturnsAsync(period);

        // Both players available on Thursday 20:00-22:00
        _playerAvailabilityRepositoryMock.Setup(r =>
            r.GetByDiscordIdsAndPeriodIdAsync(It.IsAny<List<ulong>>(), 1))
            .ReturnsAsync(new List<PlayerAvailability>
            {
                new PlayerAvailability { DiscordId = discordId1, Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) },
                new PlayerAvailability { DiscordId = discordId2, Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) }
            });

        var register = new Register
        {
            PeriodId = 1,
            CharacterRegisters = new List<CharacterRegister>
            {
                new CharacterRegister { BossId = 5, CharacterId = "c1" }
            }
        };

        // Act
        await _mergeService.MergeTeamsAsync(register);

        // Assert - merge should have happened: updateA + deleteB chars + deleteB
        _teamSlotRepositoryMock.Verify(r => r.UpdateAsync(teamA), Times.Once);
        _teamSlotCharacterRepositoryMock.Verify(r => r.DeleteByTeamSlotIdAsync(teamB.Id), Times.Once);
        _teamSlotRepositoryMock.Verify(r => r.DeleteAsync(teamB.Id), Times.Once);
    }

    [Fact]
    public async Task MergeTeamsAsync_ShouldMergeTeams_WithTemplate()
    {
        // Arrange - two teams, with template, have common time
        ulong discordId1 = 111, discordId2 = 222;
        var teamA = new TeamSlot
        {
            Id = 1, BossId = 5,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { CharacterId = "c1", DiscordId = discordId1, Job = "Hero", IsManual = false }
            }
        };
        var teamB = new TeamSlot
        {
            Id = 2, BossId = 5,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { CharacterId = "c2", DiscordId = discordId2, Job = "Bishop", IsManual = false }
            }
        };

        var template = new BossTemplate
        {
            Id = 10, BossId = 5,
            Requirements = new List<BossTemplateRequirement>
            {
                new BossTemplateRequirement { JobCategory = "任意", Count = 2, Priority = 1 }
            }
        };

        var period = new Period
        {
            Id = 1,
            StartDate = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 4, 8, 23, 59, 59, TimeSpan.Zero)
        };

        _teamSlotRepositoryMock.Setup(r => r.GetIncompleteTeamsAsync(5, 1))
            .ReturnsAsync(new List<TeamSlot> { teamA, teamB });
        _bossRepositoryMock.Setup(r => r.GetTemplatesByBossIdAsync(5))
            .ReturnsAsync(new List<BossTemplate> { template });
        _bossRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Boss>
        {
            new Boss { Id = 5, RequireMembers = 6 }
        });
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<JobCategory>
        {
            new JobCategory { CategoryName = "任意", JobName = "Hero" },
            new JobCategory { CategoryName = "任意", JobName = "Bishop" }
        });
        _periodQueryMock.Setup(q => q.GetByIdAsync(1)).ReturnsAsync(period);

        _playerAvailabilityRepositoryMock.Setup(r =>
            r.GetByDiscordIdsAndPeriodIdAsync(It.IsAny<List<ulong>>(), 1))
            .ReturnsAsync(new List<PlayerAvailability>
            {
                new PlayerAvailability { DiscordId = discordId1, Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) },
                new PlayerAvailability { DiscordId = discordId2, Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) }
            });

        var register = new Register
        {
            PeriodId = 1,
            CharacterRegisters = new List<CharacterRegister>
            {
                new CharacterRegister { BossId = 5, CharacterId = "c1" }
            }
        };

        // Act
        await _mergeService.MergeTeamsAsync(register);

        // Assert
        _teamSlotRepositoryMock.Verify(r => r.UpdateAsync(teamA), Times.Once);
        _teamSlotCharacterRepositoryMock.Verify(r => r.DeleteByTeamSlotIdAsync(teamB.Id), Times.Once);
        _teamSlotRepositoryMock.Verify(r => r.DeleteAsync(teamB.Id), Times.Once);
    }

    [Fact]
    public async Task MergeTeamsAsync_ShouldSkipMerge_WhenSameDiscordIdInBothTeams()
    {
        // 同一玩家在兩個隊伍不應合併
        ulong sameDiscordId = 111;
        var teamA = new TeamSlot
        {
            Id = 1, BossId = 5,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { CharacterId = "c1", DiscordId = sameDiscordId, IsManual = false }
            }
        };
        var teamB = new TeamSlot
        {
            Id = 2, BossId = 5,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { CharacterId = "c2", DiscordId = sameDiscordId, IsManual = false } // same!
            }
        };

        _teamSlotRepositoryMock.Setup(r => r.GetIncompleteTeamsAsync(5, 1))
            .ReturnsAsync(new List<TeamSlot> { teamA, teamB });
        _bossRepositoryMock.Setup(r => r.GetTemplatesByBossIdAsync(5)).ReturnsAsync(new List<BossTemplate>());
        _bossRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Boss>
        {
            new Boss { Id = 5, RequireMembers = 6 }
        });
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<JobCategory>());
        _playerAvailabilityRepositoryMock.Setup(r =>
            r.GetByDiscordIdsAndPeriodIdAsync(It.IsAny<List<ulong>>(), 1))
            .ReturnsAsync(new List<PlayerAvailability>());

        var register = new Register
        {
            PeriodId = 1,
            CharacterRegisters = new List<CharacterRegister> { new CharacterRegister { BossId = 5 } }
        };

        // Act
        await _mergeService.MergeTeamsAsync(register);

        // Assert - no merge (same player in both teams)
        _teamSlotRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<TeamSlot>()), Times.Never);
    }

    [Fact]
    public async Task MergeTeamsAsync_ShouldSkipMerge_WhenTotalMembersExceedLimit()
    {
        // requireMembers = 2, but combined = 3 → skip
        var teamA = new TeamSlot
        {
            Id = 1, BossId = 5,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { CharacterId = "c1", DiscordId = 111, IsManual = false },
                new TeamSlotCharacter { CharacterId = "c2", DiscordId = 222, IsManual = false }
            }
        };
        var teamB = new TeamSlot
        {
            Id = 2, BossId = 5,
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter { CharacterId = "c3", DiscordId = 333, IsManual = false }
            }
        };

        _teamSlotRepositoryMock.Setup(r => r.GetIncompleteTeamsAsync(5, 1))
            .ReturnsAsync(new List<TeamSlot> { teamA, teamB });
        _bossRepositoryMock.Setup(r => r.GetTemplatesByBossIdAsync(5)).ReturnsAsync(new List<BossTemplate>());
        _bossRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Boss>
        {
            new Boss { Id = 5, RequireMembers = 2 } // max 2 members
        });
        _jobCategoryRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<JobCategory>());
        _playerAvailabilityRepositoryMock.Setup(r =>
            r.GetByDiscordIdsAndPeriodIdAsync(It.IsAny<List<ulong>>(), 1))
            .ReturnsAsync(new List<PlayerAvailability>());

        var register = new Register
        {
            PeriodId = 1,
            CharacterRegisters = new List<CharacterRegister> { new CharacterRegister { BossId = 5 } }
        };

        // Act
        await _mergeService.MergeTeamsAsync(register);

        // Assert - no merge (combined 3 > limit 2)
        _teamSlotRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<TeamSlot>()), Times.Never);
    }
}
