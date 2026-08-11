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
    private readonly Mock<ISystemConfigService> _systemConfigServiceMock;
    private readonly Mock<IBossRepository> _bossRepositoryMock;
    private readonly Mock<ICharacterQuery> _characterQueryMock;
    private readonly RegisterService _registerService;

    public RegisterServiceUpdateDeleteTests()
    {
        _periodQueryMock = new Mock<IPeriodQuery>();
        _playerRegisterRepositoryMock = new Mock<IPlayerRegisterRepository>();
        _characterRegisterRepositoryMock = new Mock<ICharacterRegisterRepository>();
        _playerAvailabilityRepositoryMock = new Mock<IPlayerAvailabilityRepository>();
        _teamSlotCharacterRepositoryMock = new Mock<ITeamSlotCharacterRepository>();
        _systemConfigServiceMock = new Mock<ISystemConfigService>();
        _bossRepositoryMock = new Mock<IBossRepository>();
        _bossRepositoryMock.Setup(b => b.GetAllAsync()).ReturnsAsync(new List<Boss>());
        _characterQueryMock = new Mock<ICharacterQuery>();
        _characterQueryMock.Setup(q => q.GetByDiscordIdAsync(It.IsAny<ulong>()))
            .ReturnsAsync(new List<Character>());

        _registerService = new RegisterService(
            _periodQueryMock.Object,
            _playerRegisterRepositoryMock.Object,
            _characterRegisterRepositoryMock.Object,
            _playerAvailabilityRepositoryMock.Object,
            _teamSlotCharacterRepositoryMock.Object,
            _systemConfigServiceMock.Object,
            _bossRepositoryMock.Object,
            _characterQueryMock.Object,
            new Mock<IPlayerAvailabilityStandingRepository>().Object,
            new Mock<ICharacterRepository>().Object
        );
    }

    private void SetupDeadlineNotPassed()
    {
        var period = new Period { StartDate = DateTimeOffset.Now.AddDays(10) };
        var config = new SystemConfig
        {
            DeadlineDayOfWeek = DayOfWeek.Wednesday,
            DeadlineTime = new TimeSpan(23, 59, 59)
        };
        _systemConfigServiceMock.Setup(s => s.GetAsync()).ReturnsAsync(config);
        _periodQueryMock.Setup(p => p.GetActivePeriodAsync()).ReturnsAsync(period);
    }

    [Fact]
    public async Task UpdateAsync_ShouldReplaceAvailabilitiesAndUpdateRegisters()
    {
        // Arrange
        SetupDeadlineNotPassed();
        // 伺服器由 (discordId, periodId) 查出的 registerId（本測試用與 command.Id 相同的值，維持原斷言）
        _playerRegisterRepositoryMock.Setup(r => r.GetIdAsync(It.IsAny<ulong>(), It.IsAny<int>())).ReturnsAsync(10);
        // char1/char2 屬本人、boss 1/2 存在，讓 FK 前線檢查通過
        _characterQueryMock.Setup(q => q.GetByDiscordIdAsync(It.IsAny<ulong>()))
            .ReturnsAsync(new List<Character>
            {
                new Character { Id = "char1", Name = "", Job = "" },
                new Character { Id = "char2", Name = "", Job = "" }
            });
        _bossRepositoryMock.Setup(b => b.GetAllAsync()).ReturnsAsync(new List<Boss>
        {
            new Boss { Id = 1, Name = "", RequireMembers = 6, RoundConsumption = 1 },
            new Boss { Id = 2, Name = "", RequireMembers = 6, RoundConsumption = 1 }
        });
        var command = new RegisterUpdateCommand
        {
            Id = 10,
            DiscordId = 12345,
            PeriodId = 1,
            Availabilities = new List<PlayerAvailabilityDto>
            {
                new PlayerAvailabilityDto { Weekday = 2, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(22, 0) }
            },
            DeleteCharacterRegisterIds = new List<int> { 99 },
            CharacterRegisters = new List<CharacterRegisterDto>
            {
                // existing register (has Id → update)
                new CharacterRegisterDto { Id = 1, CharacterId = "char1", BossId = 1, Rounds = 1 },
                // new register (no Id → create)
                new CharacterRegisterDto { CharacterId = "char2", BossId = 2, Rounds = 1 }
            }
        };

        // Act
        await _registerService.UpdateAsync(command);

        // Assert
        _playerRegisterRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Register>()), Times.Once);
        _playerAvailabilityRepositoryMock.Verify(r => r.DeleteByPlayerRegisterIdAsync(10), Times.Once);
        _playerAvailabilityRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<PlayerAvailability>()), Times.Once);
        _characterRegisterRepositoryMock.Verify(r => r.DeleteAsync(99, 10), Times.Once);
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
        _periodQueryMock.Setup(p => p.GetActivePeriodAsync()).ReturnsAsync(period);

        // Act & Assert
        await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(() =>
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
        _periodQueryMock.Setup(p => p.GetActivePeriodAsync()).ReturnsAsync(period);

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
        _periodQueryMock.Setup(p => p.GetActivePeriodAsync()).ReturnsAsync((Period?)null);

        // Act
        await _registerService.DeleteAsync(12345, 1);

        // Assert - no delete calls should happen
        _teamSlotCharacterRepositoryMock.Verify(r =>
            r.DeleteByDiscordIdAndPeriodAsync(It.IsAny<ulong>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()),
            Times.Never);
    }

}
