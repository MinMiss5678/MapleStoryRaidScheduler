using Application.DTOs;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

/// <summary>RegisterService 的 UpdateAsync / DeleteAsync / GetLastAsync 分支測試</summary>
public class RegisterServiceUpdateDeleteTests
{
    private readonly Mock<IPeriodQuery> _periodQueryMock;
    private readonly Mock<IPlayerRegisterRepository> _playerRegisterRepositoryMock;
    private readonly Mock<ICharacterRegisterRepository> _characterRegisterRepositoryMock;
    private readonly Mock<IPlayerAvailabilityRepository> _playerAvailabilityRepositoryMock;
    private readonly Mock<ITeamSlotCharacterRepository> _teamSlotCharacterRepositoryMock;
    private readonly Mock<ITeamSlotAutoAssignService> _autoAssignServiceMock;
    private readonly Mock<ISystemConfigService> _systemConfigServiceMock;
    private readonly RegisterService _registerService;

    public RegisterServiceUpdateDeleteTests()
    {
        _periodQueryMock = new Mock<IPeriodQuery>();
        _playerRegisterRepositoryMock = new Mock<IPlayerRegisterRepository>();
        _characterRegisterRepositoryMock = new Mock<ICharacterRegisterRepository>();
        _playerAvailabilityRepositoryMock = new Mock<IPlayerAvailabilityRepository>();
        _teamSlotCharacterRepositoryMock = new Mock<ITeamSlotCharacterRepository>();
        _autoAssignServiceMock = new Mock<ITeamSlotAutoAssignService>();
        _systemConfigServiceMock = new Mock<ISystemConfigService>();

        _registerService = new RegisterService(
            _periodQueryMock.Object,
            _playerRegisterRepositoryMock.Object,
            _characterRegisterRepositoryMock.Object,
            _playerAvailabilityRepositoryMock.Object,
            _teamSlotCharacterRepositoryMock.Object,
            _autoAssignServiceMock.Object,
            _systemConfigServiceMock.Object
        );
    }

    private void SetupDeadlineNotPassed()
    {
        var period = new Period { StartDate = DateTimeOffset.Now.AddDays(1) };
        var config = new SystemConfig
        {
            DeadlineDayOfWeek = DayOfWeek.Wednesday,
            DeadlineTime = new TimeSpan(23, 59, 59)
        };
        _systemConfigServiceMock.Setup(s => s.GetAsync()).ReturnsAsync(config);
        _periodQueryMock.Setup(p => p.GetByNowAsync()).ReturnsAsync(period);
    }

    [Fact]
    public async Task UpdateAsync_ShouldReplaceAvailabilitiesAndUpdateRegisters()
    {
        // Arrange
        SetupDeadlineNotPassed();
        var command = new RegisterUpdateCommand
        {
            Id = 10,
            DiscordId = 12345,
            PeriodId = 1,
            Availabilities = new List<PlayerAvailability>
            {
                new PlayerAvailability { Weekday = 2, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) }
            },
            DeleteCharacterRegisterIds = new List<int> { 99 },
            CharacterRegisters = new List<CharacterRegister>
            {
                // existing register (has Id → update)
                new CharacterRegister { Id = 1, CharacterId = "char1", BossId = 1, Rounds = 1 },
                // new register (no Id → create)
                new CharacterRegister { CharacterId = "char2", BossId = 2, Rounds = 1 }
            }
        };

        // Act
        await _registerService.UpdateAsync(command);

        // Assert
        _playerRegisterRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Register>()), Times.Once);
        _playerAvailabilityRepositoryMock.Verify(r => r.DeleteByPlayerRegisterIdAsync(10), Times.Once);
        _playerAvailabilityRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<PlayerAvailability>()), Times.Once);
        _characterRegisterRepositoryMock.Verify(r => r.DeleteAsync(99), Times.Once);
        _characterRegisterRepositoryMock.Verify(r => r.UpdateAsync(It.Is<CharacterRegister>(c => c.Id == 1)), Times.Once);
        _characterRegisterRepositoryMock.Verify(r => r.CreateAsync(It.Is<CharacterRegister>(c => c.CharacterId == "char2")), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenPastDeadline()
    {
        // Arrange - deadline in the past
        var period = new Period { StartDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var config = new SystemConfig
        {
            DeadlineDayOfWeek = DayOfWeek.Wednesday,
            DeadlineTime = new TimeSpan(23, 59, 59)
        };
        _systemConfigServiceMock.Setup(s => s.GetAsync()).ReturnsAsync(config);
        _periodQueryMock.Setup(p => p.GetByNowAsync()).ReturnsAsync(period);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _registerService.UpdateAsync(new RegisterUpdateCommand()));
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteTeamSlotCharactersAndRegister()
    {
        // Arrange
        ulong discordId = 12345;
        int registerId = 100;
        var period = new Period
        {
            StartDate = new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 4, 16, 23, 59, 59, TimeSpan.Zero)
        };
        _periodQueryMock.Setup(p => p.GetByNowAsync()).ReturnsAsync(period);

        // Act
        await _registerService.DeleteAsync(discordId, registerId);

        // Assert
        _teamSlotCharacterRepositoryMock.Verify(r =>
            r.DeleteByDiscordIdAndPeriodAsync(discordId, period.StartDate, period.EndDate), Times.Once);
        _characterRegisterRepositoryMock.Verify(r => r.DeleteByPlayerRegisterIdAsync(registerId), Times.Once);
        _playerRegisterRepositoryMock.Verify(r => r.DeleteAsync(discordId, registerId), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ShouldReturnEarly_WhenNoPeriod()
    {
        // Arrange
        _periodQueryMock.Setup(p => p.GetByNowAsync()).ReturnsAsync((Period?)null);

        // Act
        await _registerService.DeleteAsync(12345, 1);

        // Assert - no delete calls should happen
        _teamSlotCharacterRepositoryMock.Verify(r =>
            r.DeleteByDiscordIdAndPeriodAsync(It.IsAny<ulong>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()),
            Times.Never);
    }

}
