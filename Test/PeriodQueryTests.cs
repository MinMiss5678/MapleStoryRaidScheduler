using Application.Interface;
using Infrastructure.Dapper;
using System.Data;
using Domain.Entities;
using Infrastructure.Query;
using Moq;
using Utils.SqlBuilder;
using Xunit;

namespace Test;

public class PeriodQueryTests
{
    private readonly Mock<DbContext> _dbContextMock;
    private readonly PeriodQuery _periodQuery;

    public PeriodQueryTests()
    {
        var conn = new Mock<IDbConnection>().Object;
        _dbContextMock = new Mock<DbContext>(conn);
        _periodQuery = new PeriodQuery(_dbContextMock.Object);
    }

    [Fact]
    public async Task GetPeriodIdByNowAsync_ShouldReturnIdFromCurrentPeriod()
    {
        // Arrange
        var period = new Period { Id = 10, StartDate = DateTimeOffset.UtcNow, EndDate = DateTimeOffset.UtcNow.AddDays(7) };
        _dbContextMock.Setup(u => u.QuerySingleOrDefaultAsync<Period>(It.IsAny<QueryBuilder>()))
            .ReturnsAsync(period);

        // Act
        var result = await _periodQuery.GetPeriodIdByNowAsync();

        // Assert
        Assert.Equal(10, result);
    }

    [Fact]
    public async Task GetPeriodIdByDateAsync_ShouldReturnCorrectId()
    {
        // Arrange
        var targetDate = new DateTimeOffset(new DateTime(2024, 3, 22));
        _dbContextMock.Setup(u => u.QuerySingleOrDefaultAsync<int?>(It.IsAny<QueryBuilder>()))
            .ReturnsAsync(5);

        // Act
        var result = await _periodQuery.GetPeriodIdByDateAsync(targetDate);

        // Assert
        Assert.Equal(5, result);
    }

    [Fact]
    public async Task GetLastPeriodIdAsync_ShouldSkipLatestAndReturnPrevious()
    {
        // Arrange
        _dbContextMock.Setup(u => u.QuerySingleOrDefaultAsync<int?>(It.IsAny<QueryBuilder>()))
            .ReturnsAsync(8);

        // Act
        var result = await _periodQuery.GetLastPeriodIdAsync();

        // Assert
        Assert.Equal(8, result);
        // Note: QueryBuilder building check could be added here if needed to verify Offset(1)
    }

    [Fact]
    public async Task GetByNowAsync_ShouldReturnLatestPeriod()
    {
        // Arrange
        var latest = new Period { Id = 1, StartDate = DateTimeOffset.UtcNow };
        _dbContextMock.Setup(u => u.QuerySingleOrDefaultAsync<Period>(It.IsAny<QueryBuilder>()))
            .ReturnsAsync(latest);

        // Act
        var result = await _periodQuery.GetByNowAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
    }
}
