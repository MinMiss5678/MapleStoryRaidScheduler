using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class RegisterServiceTests
{
    private readonly Mock<IPeriodQuery> _periodQueryMock;
    private readonly Mock<IPlayerRegisterRepository> _playerRegisterRepositoryMock;
    private readonly Mock<IPlayerRegisterQuery> _playerRegisterQueryMock;
    private readonly Mock<ICharacterRegisterRepository> _characterRegisterRepositoryMock;
    private readonly Mock<IPlayerAvailabilityRepository> _playerAvailabilityRepositoryMock;
    private readonly Mock<ITeamSlotService> _teamSlotServiceMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<ISystemConfigService> _systemConfigServiceMock;
    private readonly RegisterService _registerService;

    public RegisterServiceTests()
    {
        _periodQueryMock = new Mock<IPeriodQuery>();
        _playerRegisterRepositoryMock = new Mock<IPlayerRegisterRepository>();
        _playerRegisterQueryMock = new Mock<IPlayerRegisterQuery>();
        _characterRegisterRepositoryMock = new Mock<ICharacterRegisterRepository>();
        _playerAvailabilityRepositoryMock = new Mock<IPlayerAvailabilityRepository>();
        _teamSlotServiceMock = new Mock<ITeamSlotService>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _systemConfigServiceMock = new Mock<ISystemConfigService>();

        // RegisterService 目前未直接使用 UnitOfWork，無需特別設定

        _registerService = new RegisterService(
            _periodQueryMock.Object,
            _playerRegisterRepositoryMock.Object,
            _playerRegisterQueryMock.Object,
            _characterRegisterRepositoryMock.Object,
            _playerAvailabilityRepositoryMock.Object,
            new Mock<ITeamSlotCharacterRepository>().Object,
            _teamSlotServiceMock.Object,
            _unitOfWorkMock.Object,
            _systemConfigServiceMock.Object
        );
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowException_WhenPastDeadline()
    {
        // Arrange
        // 設定週期開始日期為很久以前，確保截止日期一定是過去
        var period = new Period { StartDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var config = new SystemConfig
        {
            DeadlineDayOfWeek = DayOfWeek.Wednesday,
            DeadlineTime = new TimeSpan(23, 59, 59)
        };
        _systemConfigServiceMock.Setup(s => s.GetAsync()).ReturnsAsync(config);
        _periodQueryMock.Setup(p => p.GetByNowAsync()).ReturnsAsync(period);

        var register = new Register
        {
            Availabilities = new List<PlayerAvailability>(),
            CharacterRegisters = new List<CharacterRegister>()
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<Exception>(() => _registerService.CreateAsync(register));
        Assert.Equal("目前已超過報名截止時間，無法報名。", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_ShouldCallRepositories_WhenWithinDeadline()
    {
        // Arrange
        var period = new Period { StartDate = DateTimeOffset.Now.AddDays(1) };
        var config = new SystemConfig
        {
            DeadlineDayOfWeek = DayOfWeek.Wednesday,
            DeadlineTime = new TimeSpan(23, 59, 59)
        };
        _systemConfigServiceMock.Setup(s => s.GetAsync()).ReturnsAsync(config);
        _periodQueryMock.Setup(p => p.GetByNowAsync()).ReturnsAsync(period);

        var register = new Register
        {
            Availabilities = new List<PlayerAvailability>
            {
                new PlayerAvailability { Weekday = 1, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(12, 0) }
            },
            CharacterRegisters = new List<CharacterRegister>
            {
                new CharacterRegister { CharacterId = "char1", BossId = 1, Rounds = 1 }
            }
        };

        _playerRegisterRepositoryMock.Setup(r => r.CreateAsync(register)).ReturnsAsync(100);

        // Act
        await _registerService.CreateAsync(register);

        // Assert
        _playerRegisterRepositoryMock.Verify(r => r.CreateAsync(register), Times.Once);
        _playerAvailabilityRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<PlayerAvailability>()), Times.Once);
        _characterRegisterRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<CharacterRegister>()), Times.Once);
        _teamSlotServiceMock.Verify(t => t.AutoAssignAsync(register), Times.Once);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnCorrectDto_WhenRegisterExists()
    {
        // Arrange
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

        // Act
        var result = await _registerService.GetAsync(discordId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(playerRegisterId, result.Id);
        Assert.Single(result.CharacterRegisters);
        Assert.Equal("char1", result.CharacterRegisters[0].CharacterId);
        Assert.Single(result.Availabilities);
        Assert.Equal(1, result.Availabilities[0].Weekday);
    }
}
