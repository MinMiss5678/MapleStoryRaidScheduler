using Application.Interface;
using Domain.Entities;
using Domain.Helpers;
using Domain.Repositories;
using Application.Queries;
using Infrastructure.Services;
using Moq;
using Xunit;
using Application.DTOs;

namespace Test;

public class TeamSlotServiceTests
{
    [Fact]
    public void GetNextSlotDate_ShouldBeWithinPeriod_MonToFri_20To00()
    {
        // Arrange
        var period = new Period
        {
            StartDate = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero), // 2026-04-02 08:00 TPE
            EndDate = new DateTimeOffset(2026, 4, 8, 23, 59, 59, TimeSpan.Zero)
        };

        var availMon = new PlayerAvailability { Weekday = 1, StartTime = new TimeOnly(20, 0) };
        var availThuMidnight = new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(0, 0) };
        var availThu8PM = new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0) };

        // Act
        var resultMon = SlotDateCalculator.GetNextSlotDate(availMon, period);
        var resultThuMid = SlotDateCalculator.GetNextSlotDate(availThuMidnight, period);
        var resultThu8PM = SlotDateCalculator.GetNextSlotDate(availThu8PM, period);

        // Assert
        // GetNextSlotDate returns DateTime (local TPE), compare as DateTime
        Assert.Equal(new DateTime(2026, 4, 6, 20, 0, 0), resultMon);
        Assert.Equal(new DateTime(2026, 4, 9, 0, 0, 0), resultThuMid);
        Assert.Equal(new DateTime(2026, 4, 2, 20, 0, 0), resultThu8PM);
    }

    [Fact]
    public async Task GetBestAvailability_ShouldPreferThu8PM_OverThuMidnight()
    {
        // Arrange
        var period = new Period
        {
            StartDate = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero), // 2026-04-02 08:00 TPE
            EndDate = new DateTimeOffset(2026, 4, 8, 23, 59, 59, TimeSpan.Zero)
        };

        var register = new Register
        {
            Availabilities = new List<PlayerAvailability>
            {
                new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(0, 0) },
                new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0) }
            }
        };

        // Act
        var result = SlotDateCalculator.GetBestAvailability(register, period);

        // Assert
        Assert.Equal(20, result.StartTime.Hour);
    }

    [Fact]
    public void IsInJobCategory_ShouldReturnTrue_WhenJobMatchesCategory()
    {
        // Arrange
        var jobCategories = new Dictionary<string, HashSet<string>>
        {
            { "Warrior", new HashSet<string> { "Hero", "Paladin", "Dark Knight" } },
            { "Mage", new HashSet<string> { "Fire/Poison", "Ice/Lightning", "Bishop" } }
        };

        // Act & Assert
        Assert.True(JobCategoryHelper.IsInJobCategory("Hero", "Warrior", jobCategories));
        Assert.True(JobCategoryHelper.IsInJobCategory("Bishop", "Mage", jobCategories));
        Assert.False(JobCategoryHelper.IsInJobCategory("Hero", "Mage", jobCategories));
        Assert.False(JobCategoryHelper.IsInJobCategory("Thief", "Warrior", jobCategories));
    }

    [Fact]
    public void TryMatchTemplate_ShouldReturnMatchedCharacters_WhenRequirementsMet()
    {
        // Arrange
        var jobCategories = new Dictionary<string, HashSet<string>>
        {
            { "任意", new HashSet<string> { "Hero", "Bishop" } },
            { "Warrior", new HashSet<string> { "Hero" } },
            { "Mage", new HashSet<string> { "Bishop" } }
        };

        var template = new BossTemplate
        {
            Requirements = new List<BossTemplateRequirement>
            {
                new BossTemplateRequirement { JobCategory = "Warrior", Count = 1, Priority = 1 },
                new BossTemplateRequirement { JobCategory = "Mage", Count = 1, Priority = 2 }
            }
        };

        var members = new List<TeamSlotCharacter>
        {
            new TeamSlotCharacter { CharacterName = "P1", Job = "Hero" },
            new TeamSlotCharacter { CharacterName = "P2", Job = "Bishop" }
        };

        // Act
        var result = TeamSlotMergeService.TryMatchTemplate(members, template, jobCategories, 6);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        // 驗證成員被正確放入 slot
        Assert.Contains(result, c => c.CharacterName == "P1");
        Assert.Contains(result, c => c.CharacterName == "P2");
    }
}

public class TeamSlotAutoAssignServiceTests
{
    private readonly Mock<ITeamSlotRepository> _teamSlotRepositoryMock;
    private readonly Mock<ITeamSlotCharacterRepository> _teamSlotCharacterRepositoryMock;
    private readonly Mock<IPeriodQuery> _periodQueryMock;
    private readonly Mock<ICharacterQuery> _characterQueryMock;
    private readonly Mock<IBossRepository> _bossRepositoryMock;
    private readonly Mock<IPlayerRepository> _playerRepositoryMock;
    private readonly Mock<ITeamSlotMergeService> _mergeServiceMock;
    private readonly TeamSlotAutoAssignService _autoAssignService;

    public TeamSlotAutoAssignServiceTests()
    {
        _teamSlotRepositoryMock = new Mock<ITeamSlotRepository>();
        _teamSlotCharacterRepositoryMock = new Mock<ITeamSlotCharacterRepository>();
        _periodQueryMock = new Mock<IPeriodQuery>();
        _characterQueryMock = new Mock<ICharacterQuery>();
        _bossRepositoryMock = new Mock<IBossRepository>();
        _playerRepositoryMock = new Mock<IPlayerRepository>();
        _mergeServiceMock = new Mock<ITeamSlotMergeService>();

        _autoAssignService = new TeamSlotAutoAssignService(
            _teamSlotRepositoryMock.Object,
            _teamSlotCharacterRepositoryMock.Object,
            _periodQueryMock.Object,
            _characterQueryMock.Object,
            _bossRepositoryMock.Object,
            _playerRepositoryMock.Object,
            _mergeServiceMock.Object
        );
    }

    [Fact]
    public async Task AutoAssignAsync_ShouldAssignToExistingSlot_WhenMatchFound()
    {
        // Arrange
        var discordId = 12345UL;
        var periodId = 1;
        var bossId = 10;
        var characterId = "char1";

        var register = new Register
        {
            DiscordId = discordId,
            PeriodId = periodId,
            CharacterRegisters = new List<CharacterRegister>
            {
                new CharacterRegister { CharacterId = characterId, BossId = bossId, Rounds = 1 }
            },
            Availabilities = new List<PlayerAvailability>
            {
                new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(22, 0) }
            }
        };

        var slotDateTime = new DateTimeOffset(2024, 5, 23, 12, 0, 0, TimeSpan.Zero);
        var existingTeamSlot = new TeamSlot
        {
            Id = 100,
            BossId = bossId,
            SlotDateTime = slotDateTime,
            Characters = new List<TeamSlotCharacter>()
        };

        _teamSlotRepositoryMock.Setup(r => r.GetByPeriodIdAsync(periodId))
            .ReturnsAsync(new List<TeamSlot> { existingTeamSlot });

        _periodQueryMock.Setup(p => p.GetByIdAsync(periodId))
            .ReturnsAsync(new Period
            {
                Id = periodId,
                StartDate = new DateTimeOffset(2024, 5, 23, 0, 0, 0, TimeSpan.Zero),
                EndDate = new DateTimeOffset(2024, 5, 30, 0, 0, 0, TimeSpan.Zero)
            });

        _characterQueryMock.Setup(q => q.GetByDiscordIdAsync(discordId))
            .ReturnsAsync(new List<Character>
            {
                new Character { Id = characterId, Name = "Hero", Job = "Warrior", AttackPower = 1000 }
            });

        _playerRepositoryMock.Setup(r => r.GetAsync(discordId))
            .ReturnsAsync(new Player { DiscordId = discordId, DiscordName = "Player1" });

        // Act
        await _autoAssignService.AutoAssignAsync(register);

        // Assert
        _teamSlotCharacterRepositoryMock.Verify(r => r.CreateAsync(It.Is<TeamSlotCharacter>(c =>
            c.CharacterId == characterId && c.DiscordId == discordId)), Times.Once);
    }

    [Fact]
    public async Task AutoAssignAsync_ShouldCreateNewTeam_OnThursday20_WhenMonToFri_20To00()
    {
        // Arrange
        var discordId = 24680UL;
        var periodId = 2;
        var bossId = 20;
        var characterId = "charX";

        var register = new Register
        {
            DiscordId = discordId,
            PeriodId = periodId,
            CharacterRegisters = new List<CharacterRegister>
            {
                new CharacterRegister { CharacterId = characterId, BossId = bossId, Rounds = 2 }
            },
            Availabilities = new List<PlayerAvailability>
            {
                new PlayerAvailability { Weekday = 1, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(0, 0) },
                new PlayerAvailability { Weekday = 2, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(0, 0) },
                new PlayerAvailability { Weekday = 3, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(0, 0) },
                new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(0, 0) },
                new PlayerAvailability { Weekday = 5, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(0, 0) }
            }
        };

        _teamSlotRepositoryMock.Setup(r => r.GetByPeriodIdAsync(periodId))
            .ReturnsAsync(new List<TeamSlot>());

        _periodQueryMock.Setup(p => p.GetByIdAsync(periodId))
            .ReturnsAsync(new Period
            {
                Id = periodId,
                StartDate = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero),
                EndDate = new DateTimeOffset(2026, 4, 8, 23, 59, 59, TimeSpan.Zero)
            });

        _characterQueryMock.Setup(q => q.GetByDiscordIdAsync(discordId))
            .ReturnsAsync(new List<Character>
            {
                new Character { Id = characterId, Name = "Mage", Job = "Bishop", AttackPower = 500 }
            });

        _playerRepositoryMock.Setup(r => r.GetAsync(discordId))
            .ReturnsAsync(new Player { DiscordId = discordId, DiscordName = "Player2" });

        int capturedTeamSlotId = 0;
        _teamSlotRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<TeamSlot>()))
            .Callback<TeamSlot>(ts =>
            {
                capturedTeamSlotId++;
                ts.Id = capturedTeamSlotId;
            })
            .ReturnsAsync(() => capturedTeamSlotId);

        // Act
        await _autoAssignService.AutoAssignAsync(register);

        // Assert
        _teamSlotRepositoryMock.Verify(r => r.CreateAsync(It.Is<TeamSlot>(ts =>
            ts.BossId == bossId &&
            ts.SlotDateTime == new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero)
        )), Times.Once);
    }

    [Fact]
    public async Task AutoAssignAsync_ShouldCreateNewSlot_WhenTeamNotFull()
    {
        // Arrange
        var discordId = 12345UL;
        var periodId = 1;
        var bossId = 10;
        var characterId = "char1";

        var register = new Register
        {
            DiscordId = discordId,
            PeriodId = periodId,
            CharacterRegisters = new List<CharacterRegister>
            {
                new CharacterRegister { CharacterId = characterId, BossId = bossId, Rounds = 1 }
            },
            Availabilities = new List<PlayerAvailability>
            {
                new PlayerAvailability { Weekday = 4, StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(22, 0) }
            }
        };

        var slotDateTime = new DateTimeOffset(2024, 5, 23, 20, 0, 0, TimeSpan.FromHours(8));
        var existingTeamSlot = new TeamSlot
        {
            Id = 100,
            BossId = bossId,
            SlotDateTime = slotDateTime.ToOffset(TimeSpan.Zero),
            Characters = new List<TeamSlotCharacter>
            {
                new TeamSlotCharacter
                {
                    Id = 1,
                    CharacterId = "other_char",
                    Job = "Support",
                    TeamSlotId = 100
                }
            }
        };

        _teamSlotRepositoryMock.Setup(r => r.GetByPeriodIdAsync(periodId))
            .ReturnsAsync(new List<TeamSlot> { existingTeamSlot });

        _periodQueryMock.Setup(p => p.GetByIdAsync(periodId))
            .ReturnsAsync(new Period
            {
                Id = periodId,
                StartDate = new DateTimeOffset(2024, 5, 23, 0, 0, 0, TimeSpan.Zero),
                EndDate = new DateTimeOffset(2024, 5, 30, 0, 0, 0, TimeSpan.Zero)
            });

        _characterQueryMock.Setup(q => q.GetByDiscordIdAsync(discordId))
            .ReturnsAsync(new List<Character>
            {
                new Character { Id = characterId, Name = "Dealer", Job = "NightLord", AttackPower = 2000 }
            });

        _bossRepositoryMock.Setup(r => r.GetTemplatesByBossIdAsync(bossId))
            .ReturnsAsync(new List<BossTemplate>());

        // Act
        await _autoAssignService.AutoAssignAsync(register);

        // Assert
        _teamSlotRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<TeamSlot>()), Times.Never);
        _teamSlotCharacterRepositoryMock.Verify(r => r.CreateAsync(It.Is<TeamSlotCharacter>(c =>
            c.CharacterId == characterId && c.TeamSlotId == 100)), Times.Once);

        Assert.Contains(existingTeamSlot.Characters, c => c.CharacterId == characterId);
    }
}
