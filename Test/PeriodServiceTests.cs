using Application.Exceptions;
using Application.Queries;
using Domain.Entities;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class PeriodServiceTests
{
    private readonly Mock<IPeriodQuery> _periodQueryMock;
    private readonly PeriodService _periodService;

    public PeriodServiceTests()
    {
        _periodQueryMock = new Mock<IPeriodQuery>();
        _periodService = new PeriodService(_periodQueryMock.Object);
    }

    [Fact]
    public async Task GetByNowAsync_ShouldReturnDto_WhenPeriodExists()
    {
        // Arrange
        var period = new Period
        {
            Id = 5,
            StartDate = new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 4, 16, 23, 59, 59, TimeSpan.Zero)
        };
        _periodQueryMock.Setup(q => q.GetByNowAsync()).ReturnsAsync(period);

        // Act
        var result = await _periodService.GetByNowAsync();

        // Assert
        Assert.Equal(5, result.Id);
        // 檢查日期轉換為 UTC+8
        Assert.Equal(new DateTime(2026, 4, 10, 8, 0, 0), result.StartDate);
        Assert.Equal(new DateTime(2026, 4, 17, 7, 59, 59), result.EndDate);
    }

    [Fact]
    public async Task GetByNowAsync_ShouldThrowNotFoundException_WhenNoPeriodExists()
    {
        // Arrange
        _periodQueryMock.Setup(q => q.GetByNowAsync()).ReturnsAsync((Period?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _periodService.GetByNowAsync());
    }
}
