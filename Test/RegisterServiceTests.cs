using Application.DTOs;
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
        _periodQueryMock.Setup(p => p.GetActivePeriodAsync()).ReturnsAsync(period);

        var command = new RegisterCreateCommand
        {
            Availabilities = new List<PlayerAvailabilityDto>(),
            CharacterRegisters = new List<CharacterRegisterDto>()
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _registerService.CreateAsync(command));
        Assert.Equal("目前已超過報名截止時間。", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_ShouldCallRepositories_WhenWithinDeadline()
    {
        // Arrange
        var period = new Period { StartDate = DateTimeOffset.Now.AddDays(10) };
        var config = new SystemConfig
        {
            DeadlineDayOfWeek = DayOfWeek.Wednesday,
            DeadlineTime = new TimeSpan(23, 59, 59)
        };
        _systemConfigServiceMock.Setup(s => s.GetAsync()).ReturnsAsync(config);
        _periodQueryMock.Setup(p => p.GetActivePeriodAsync()).ReturnsAsync(period);

        var command = new RegisterCreateCommand
        {
            Availabilities = new List<PlayerAvailabilityDto>
            {
                new PlayerAvailabilityDto { Weekday = 1, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(12, 0) }
            },
            CharacterRegisters = new List<CharacterRegisterDto>
            {
                new CharacterRegisterDto { CharacterId = "char1", BossId = 1, Rounds = 1 }
            }
        };

        _playerRegisterRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<Register>())).ReturnsAsync(100);

        // Act
        await _registerService.CreateAsync(command);

        // Assert
        _playerRegisterRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<Register>()), Times.Once);
        _playerAvailabilityRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<PlayerAvailability>()), Times.Once);
        _characterRegisterRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<CharacterRegister>()), Times.Once);
        _autoAssignServiceMock.Verify(t => t.AutoAssignAsync(It.IsAny<Register>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowException_WhenAlreadyRegistered()
    {
        // Arrange
        var period = new Period { StartDate = DateTimeOffset.Now.AddDays(10) };
        var config = new SystemConfig
        {
            DeadlineDayOfWeek = DayOfWeek.Wednesday,
            DeadlineTime = new TimeSpan(23, 59, 59)
        };
        _systemConfigServiceMock.Setup(s => s.GetAsync()).ReturnsAsync(config);
        _periodQueryMock.Setup(p => p.GetActivePeriodAsync()).ReturnsAsync(period);

        var command = new RegisterCreateCommand
        {
            DiscordId = 123456789UL,
            PeriodId = 1,
            Availabilities = new List<PlayerAvailabilityDto>(),
            CharacterRegisters = new List<CharacterRegisterDto>()
        };

        _playerRegisterRepositoryMock
            .Setup(r => r.ExistAsync(command.DiscordId, command.PeriodId))
            .ReturnsAsync(true);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _registerService.CreateAsync(command));
        Assert.Equal("您已完成本期報名，請勿重複提交。", exception.Message);
        _playerRegisterRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<Register>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ShouldUseServerResolvedRegisterId_NotClientProvidedId()
    {
        // Arrange：報名開放中
        _systemConfigServiceMock.Setup(s => s.GetAsync()).ReturnsAsync(new SystemConfig
        {
            DeadlineDayOfWeek = DayOfWeek.Wednesday,
            DeadlineTime = new TimeSpan(23, 59, 59)
        });
        _periodQueryMock.Setup(p => p.GetActivePeriodAsync())
            .ReturnsAsync(new Period { StartDate = DateTimeOffset.Now.AddDays(10) });

        const int realRegisterId = 5;    // 伺服器由 (discordId, periodId) 查出的、呼叫者自己的 id
        const int fakeRegisterId = 999;  // 前端亂傳的（可能是別人的）
        _playerRegisterRepositoryMock
            .Setup(r => r.GetIdAsync(It.IsAny<ulong>(), It.IsAny<int>()))
            .ReturnsAsync(realRegisterId);

        var command = new RegisterUpdateCommand
        {
            Id = fakeRegisterId,            // ← 攻擊者傳別人的 id
            DiscordId = 123456789UL,
            PeriodId = 1,
            Availabilities = new List<PlayerAvailabilityDto>
            {
                new PlayerAvailabilityDto { Weekday = 1, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(12, 0) }
            },
            DeleteCharacterRegisterIds = new List<int> { 42 }
        };

        // Act
        await _registerService.UpdateAsync(command);

        // Assert：全程用 realRegisterId(5)，完全不碰 fakeRegisterId(999)
        _playerRegisterRepositoryMock.Verify(r => r.GetIdAsync(command.DiscordId, command.PeriodId), Times.Once);
        _playerAvailabilityRepositoryMock.Verify(r => r.DeleteByPlayerRegisterIdAsync(realRegisterId), Times.Once);
        _playerAvailabilityRepositoryMock.Verify(r => r.DeleteByPlayerRegisterIdAsync(fakeRegisterId), Times.Never);
        _characterRegisterRepositoryMock.Verify(r => r.DeleteAsync(42, realRegisterId), Times.Once);
        _playerAvailabilityRepositoryMock.Verify(
            r => r.CreateAsync(It.Is<PlayerAvailability>(a => a.PlayerRegisterId == realRegisterId)), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenNoRegistrationFound()
    {
        // Arrange：報名開放，但查不到呼叫者的 registerId
        _systemConfigServiceMock.Setup(s => s.GetAsync()).ReturnsAsync(new SystemConfig
        {
            DeadlineDayOfWeek = DayOfWeek.Wednesday,
            DeadlineTime = new TimeSpan(23, 59, 59)
        });
        _periodQueryMock.Setup(p => p.GetActivePeriodAsync())
            .ReturnsAsync(new Period { StartDate = DateTimeOffset.Now.AddDays(10) });
        _playerRegisterRepositoryMock
            .Setup(r => r.GetIdAsync(It.IsAny<ulong>(), It.IsAny<int>()))
            .ReturnsAsync((int?)null);

        var command = new RegisterUpdateCommand { Id = 999, DiscordId = 1UL, PeriodId = 1 };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _registerService.UpdateAsync(command));
        Assert.Equal("找不到本期報名，無法更新。", ex.Message);
        _playerAvailabilityRepositoryMock.Verify(r => r.DeleteByPlayerRegisterIdAsync(It.IsAny<int>()), Times.Never);
    }

}
