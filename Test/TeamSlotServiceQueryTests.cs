using Application.DTOs;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class TeamSlotServiceQueryTests
{
    private readonly Mock<ITeamSlotRepository> _teamSlotRepositoryMock;
    private readonly Mock<ITeamSlotQuery> _teamSlotQueryMock;
    private readonly Mock<ITeamSlotCharacterRepository> _teamSlotCharacterRepositoryMock;
    private readonly Mock<IPeriodQuery> _periodQueryMock;
    private readonly TeamSlotService _teamSlotService;

    public TeamSlotServiceQueryTests()
    {
        _teamSlotRepositoryMock = new Mock<ITeamSlotRepository>();
        _teamSlotQueryMock = new Mock<ITeamSlotQuery>();
        _teamSlotCharacterRepositoryMock = new Mock<ITeamSlotCharacterRepository>();
        _periodQueryMock = new Mock<IPeriodQuery>();

        _teamSlotService = new TeamSlotService(
            _teamSlotRepositoryMock.Object,
            _teamSlotQueryMock.Object,
            _teamSlotCharacterRepositoryMock.Object,
            _periodQueryMock.Object);
    }

    private Period CreatePeriod() => new Period
    {
        Id = 1,
        StartDate = new DateTimeOffset(2026, 4, 3, 0, 0, 0, TimeSpan.Zero),
        EndDate = new DateTimeOffset(2026, 4, 9, 23, 59, 59, TimeSpan.Zero)
    };

    [Fact]
    public async Task GetByBossIdAsync_ShouldReturnGroupedTeamSlots()
    {
        // Arrange
        int bossId = 5;
        var period = CreatePeriod();
        var slotDateTime = new DateTimeOffset(2026, 4, 3, 12, 0, 0, TimeSpan.Zero);

        var dtos = new List<TeamSlotCharacterDto>
        {
            new TeamSlotCharacterDto
            {
                TeamSlotId = 1, BossId = bossId, BossName = "Zakum",
                SlotDateTime = slotDateTime, TeamSlotCharacterId = 10,
                DiscordId = 12345, DiscordName = "Player1", CharacterId = "c1",
                CharacterName = "Hero", Job = "Warrior", AttackPower = 1000, Rounds = 1
            },
            new TeamSlotCharacterDto
            {
                TeamSlotId = 1, BossId = bossId, BossName = "Zakum",
                SlotDateTime = slotDateTime, TeamSlotCharacterId = 11,
                DiscordId = 67890, DiscordName = "Player2", CharacterId = "c2",
                CharacterName = "Bishop", Job = "Mage", AttackPower = 900, Rounds = 1
            }
        };

        _periodQueryMock.Setup(q => q.GetByNowAsync()).ReturnsAsync(period);
        _teamSlotQueryMock.Setup(q => q.GetByPeriodAndBossIdAsync(period, bossId)).ReturnsAsync(dtos);

        // Act
        var result = (await _teamSlotService.GetByBossIdAsync(bossId)).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(bossId, result[0].BossId);
        Assert.Equal("Zakum", result[0].BossName);
        Assert.Equal(2, result[0].Characters.Count);
    }

    [Fact]
    public async Task GetByBossIdAsync_ShouldReturnEmpty_WhenNoSlots()
    {
        // Arrange
        var period = CreatePeriod();
        _periodQueryMock.Setup(q => q.GetByNowAsync()).ReturnsAsync(period);
        _teamSlotQueryMock.Setup(q => q.GetByPeriodAndBossIdAsync(period, It.IsAny<int>()))
            .ReturnsAsync(new List<TeamSlotCharacterDto>());

        // Act
        var result = await _teamSlotService.GetByBossIdAsync(99);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByDiscordIdAsync_ShouldReturnGroupedTeamSlots()
    {
        // Arrange
        ulong discordId = 12345;
        var period = CreatePeriod();
        var slotDateTime = new DateTimeOffset(2026, 4, 4, 20, 0, 0, TimeSpan.Zero);

        var dtos = new List<TeamSlotCharacterDto>
        {
            new TeamSlotCharacterDto
            {
                TeamSlotId = 2, BossId = 7, BossName = "Chaos Root Abyss",
                SlotDateTime = slotDateTime, TeamSlotCharacterId = 20,
                DiscordId = discordId, DiscordName = "MyPlayer", CharacterId = "c3",
                CharacterName = "Bowmaster", Job = "Archer", AttackPower = 1500, Rounds = 2
            }
        };

        _periodQueryMock.Setup(q => q.GetByNowAsync()).ReturnsAsync(period);
        _teamSlotQueryMock.Setup(q => q.GetByPeriodAndDiscordIdAsync(period, discordId)).ReturnsAsync(dtos);

        // Act
        var result = (await _teamSlotService.GetByDiscordIdAsync(discordId)).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(2, result[0].Id);
        Assert.Single(result[0].Characters);
        Assert.Equal(discordId, result[0].Characters[0].DiscordId);
    }

    [Fact]
    public async Task GetByDiscordIdAsync_ShouldGroupBySlotDateTime_WhenMultipleSlots()
    {
        // Arrange
        ulong discordId = 12345;
        var period = CreatePeriod();
        var slot1 = new DateTimeOffset(2026, 4, 3, 20, 0, 0, TimeSpan.Zero);
        var slot2 = new DateTimeOffset(2026, 4, 5, 20, 0, 0, TimeSpan.Zero);

        var dtos = new List<TeamSlotCharacterDto>
        {
            new TeamSlotCharacterDto { TeamSlotId = 1, BossId = 5, BossName = "B1", SlotDateTime = slot1, TeamSlotCharacterId = 10, DiscordId = discordId },
            new TeamSlotCharacterDto { TeamSlotId = 2, BossId = 6, BossName = "B2", SlotDateTime = slot2, TeamSlotCharacterId = 20, DiscordId = discordId }
        };

        _periodQueryMock.Setup(q => q.GetByNowAsync()).ReturnsAsync(period);
        _teamSlotQueryMock.Setup(q => q.GetByPeriodAndDiscordIdAsync(period, discordId)).ReturnsAsync(dtos);

        // Act
        var result = (await _teamSlotService.GetByDiscordIdAsync(discordId)).ToList();

        // Assert
        Assert.Equal(2, result.Count);
    }
}
