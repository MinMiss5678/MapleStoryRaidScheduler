using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class TeamSlotCharacterServiceTests
{
    private readonly Mock<ITeamSlotCharacterRepository> _repoMock;
    private readonly Mock<IPeriodQuery> _periodQueryMock;
    private readonly TeamSlotCharacterService _service;

    public TeamSlotCharacterServiceTests()
    {
        _repoMock = new Mock<ITeamSlotCharacterRepository>();
        _periodQueryMock = new Mock<IPeriodQuery>();
        _service = new TeamSlotCharacterService(_repoMock.Object, _periodQueryMock.Object);
    }

    [Fact]
    public async Task DeleteByDiscordIdAndPeriodAsync_ShouldDeleteWhenPeriodExists()
    {
        // Arrange
        ulong discordId = 12345;
        var period = new Period
        {
            StartDate = new DateTimeOffset(2026, 4, 3, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 4, 9, 23, 59, 59, TimeSpan.Zero)
        };
        _periodQueryMock.Setup(q => q.GetByNowAsync()).ReturnsAsync(period);

        // Act
        await _service.DeleteByDiscordIdAndPeriodAsync(discordId);

        // Assert
        _repoMock.Verify(r => r.DeleteByDiscordIdAndPeriodAsync(discordId, period.StartDate, period.EndDate), Times.Once);
    }

    [Fact]
    public async Task DeleteByDiscordIdAndPeriodAsync_ShouldSkip_WhenNoPeriod()
    {
        // Arrange
        _periodQueryMock.Setup(q => q.GetByNowAsync()).ReturnsAsync((Period?)null);

        // Act
        await _service.DeleteByDiscordIdAndPeriodAsync(12345);

        // Assert
        _repoMock.Verify(r =>
            r.DeleteByDiscordIdAndPeriodAsync(It.IsAny<ulong>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()),
            Times.Never);
    }
}
