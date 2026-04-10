using Application.Exceptions;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class RegisterQueryServiceTests
{
    private readonly Mock<IPeriodQuery> _periodQueryMock;
    private readonly Mock<IPlayerRegisterRepository> _playerRegisterRepositoryMock;
    private readonly Mock<IPlayerRegisterQuery> _playerRegisterQueryMock;
    private readonly Mock<IPlayerAvailabilityRepository> _playerAvailabilityRepositoryMock;
    private readonly RegisterQueryService _queryService;

    public RegisterQueryServiceTests()
    {
        _periodQueryMock = new Mock<IPeriodQuery>();
        _playerRegisterRepositoryMock = new Mock<IPlayerRegisterRepository>();
        _playerRegisterQueryMock = new Mock<IPlayerRegisterQuery>();
        _playerAvailabilityRepositoryMock = new Mock<IPlayerAvailabilityRepository>();

        _queryService = new RegisterQueryService(
            _periodQueryMock.Object,
            _playerRegisterRepositoryMock.Object,
            _playerRegisterQueryMock.Object,
            _playerAvailabilityRepositoryMock.Object
        );
    }

    [Fact]
    public async Task GetAsync_ShouldThrowNotFoundException_WhenNoPeriod()
    {
        _periodQueryMock.Setup(p => p.GetPeriodIdByNowAsync()).ReturnsAsync(0);

        await Assert.ThrowsAsync<NotFoundException>(() => _queryService.GetAsync(12345UL));
    }

    [Fact]
    public async Task GetAsync_ShouldThrowNotFoundException_WhenNoRegister()
    {
        _periodQueryMock.Setup(p => p.GetPeriodIdByNowAsync()).ReturnsAsync(1);
        _playerRegisterRepositoryMock.Setup(r => r.GetListAsync(It.IsAny<ulong>(), 1))
            .ReturnsAsync(new List<PlayerCharacterRegister>());

        await Assert.ThrowsAsync<NotFoundException>(() => _queryService.GetAsync(12345UL));
    }

    [Fact]
    public async Task GetAsync_ShouldReturnCorrectDto_WhenRegisterExists()
    {
        ulong discordId = 12345;
        int periodId = 1;
        int playerRegisterId = 100;

        _periodQueryMock.Setup(p => p.GetPeriodIdByNowAsync()).ReturnsAsync(periodId);

        var playerCharacterRegisters = new List<PlayerCharacterRegister>
        {
            new PlayerCharacterRegister
            {
                Id = playerRegisterId,
                PeriodId = periodId,
                CharacterRegisterId = 200,
                CharacterId = "char1",
                Job = "Hero",
                BossId = 1,
                Rounds = 1
            }
        };
        _playerRegisterRepositoryMock.Setup(r => r.GetListAsync(discordId, periodId))
            .ReturnsAsync(playerCharacterRegisters);

        var availabilities = new List<PlayerAvailability>
        {
            new PlayerAvailability { Weekday = 1, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(12, 0) }
        };
        _playerAvailabilityRepositoryMock.Setup(r => r.GetByPlayerRegisterIdAsync(playerRegisterId))
            .ReturnsAsync(availabilities);

        var result = await _queryService.GetAsync(discordId);

        Assert.NotNull(result);
        Assert.Equal(playerRegisterId, result.Id);
        Assert.Single(result.CharacterRegisters);
        Assert.Equal("char1", result.CharacterRegisters[0].CharacterId);
        Assert.Single(result.Availabilities);
        Assert.Equal(1, result.Availabilities[0].Weekday);
    }

    [Fact]
    public async Task GetLastAsync_ShouldThrowNotFoundException_WhenNoPeriod()
    {
        _periodQueryMock.Setup(p => p.GetLastPeriodIdAsync()).ReturnsAsync(0);

        await Assert.ThrowsAsync<NotFoundException>(() => _queryService.GetLastAsync(12345UL));
    }

    [Fact]
    public async Task GetByQueryAsync_ShouldReturnEmpty_WhenNoRegisters()
    {
        var request = new Application.DTOs.RegisterGetByQueryRequest
        {
            SlotDateTime = DateTimeOffset.Now
        };

        _periodQueryMock.Setup(p => p.GetPeriodIdByDateAsync(request.SlotDateTime.Value)).ReturnsAsync(1);
        _playerRegisterQueryMock.Setup(q => q.GetByQueryAsync(request, 1))
            .ReturnsAsync(new List<PlayerRegisterSchedule>());

        var result = await _queryService.GetByQueryAsync(request);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByQueryAsync_ShouldMapToTeamSlotCharacter()
    {
        var request = new Application.DTOs.RegisterGetByQueryRequest
        {
            SlotDateTime = DateTimeOffset.Now
        };

        _periodQueryMock.Setup(p => p.GetPeriodIdByDateAsync(request.SlotDateTime.Value)).ReturnsAsync(1);
        _playerRegisterQueryMock.Setup(q => q.GetByQueryAsync(request, 1))
            .ReturnsAsync(new List<PlayerRegisterSchedule>
            {
                new PlayerRegisterSchedule
                {
                    DiscordId = 99UL,
                    DiscordName = "TestUser",
                    CharacterId = "char1",
                    CharacterName = "Hero1",
                    Job = "Hero",
                    AttackPower = 5000,
                    Rounds = 1
                }
            });

        var result = (await _queryService.GetByQueryAsync(request)).ToList();

        Assert.Single(result);
        Assert.Equal(99UL, result[0].DiscordId);
        Assert.Equal("Hero", result[0].Job);
    }
}
