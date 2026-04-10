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
    private readonly Mock<ICharacterRegisterRepository> _characterRegisterRepositoryMock;
    private readonly Mock<IPlayerAvailabilityRepository> _playerAvailabilityRepositoryMock;
    private readonly Mock<ITeamSlotAutoAssignService> _autoAssignServiceMock;
    private readonly Mock<ISystemConfigService> _systemConfigServiceMock;
    private readonly RegisterService _registerService;

    public RegisterServiceTests()
    {
        _periodQueryMock = new Mock<IPeriodQuery>();
        _playerRegisterRepositoryMock = new Mock<IPlayerRegisterRepository>();
        _characterRegisterRepositoryMock = new Mock<ICharacterRegisterRepository>();
        _playerAvailabilityRepositoryMock = new Mock<IPlayerAvailabilityRepository>();
        _autoAssignServiceMock = new Mock<ITeamSlotAutoAssignService>();
        _systemConfigServiceMock = new Mock<ISystemConfigService>();

        _registerService = new RegisterService(
            _periodQueryMock.Object,
            _playerRegisterRepositoryMock.Object,
            _characterRegisterRepositoryMock.Object,
            _playerAvailabilityRepositoryMock.Object,
            new Mock<ITeamSlotCharacterRepository>().Object,
            _autoAssignServiceMock.Object,
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
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _registerService.CreateAsync(register));
        Assert.Equal("目前已超過報名截止時間。", exception.Message);
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
        _autoAssignServiceMock.Verify(t => t.AutoAssignAsync(register), Times.Once);
    }

}
