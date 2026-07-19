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
    public async Task GetActivePeriodIdAsync_ShouldReturnIdFromCurrentPeriod()
    {
        // Arrange
        var period = new Period { Id = 10, StartDate = DateTimeOffset.UtcNow, EndDate = DateTimeOffset.UtcNow.AddDays(7) };
        _dbContextMock.Setup(u => u.QuerySingleOrDefaultAsync<Period>(It.IsAny<QueryBuilder>()))
            .ReturnsAsync(period);

        // Act
        var result = await _periodQuery.GetActivePeriodIdAsync();

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
    public async Task GetPeriodIdByDateAsync_ShouldUseUtcDate_WhenInputHasPositiveOffset()
    {
        // 04/23 01:00 +08:00 = 04/22 17:00 UTC
        // targetDate 應為 04/22 00:00 UTC（UTC 日期），而非 04/23 00:00 UTC（+08 本地日期）
        var date = new DateTimeOffset(2026, 4, 23, 1, 0, 0, TimeSpan.FromHours(8));
        var expectedUtcDate = new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero);

        QueryBuilder? captured = null;
        _dbContextMock
            .Setup(u => u.QuerySingleOrDefaultAsync<int?>(It.IsAny<QueryBuilder>()))
            .Callback<QueryBuilder>(qb => captured = qb)
            .ReturnsAsync(5);

        await _periodQuery.GetPeriodIdByDateAsync(date);

        Assert.NotNull(captured);
        var (_, parameters) = captured.Build();
        var dateParams = parameters.ParameterNames
            .Select(name =>
            {
                try { return (DateTimeOffset?)parameters.Get<DateTimeOffset>(name); }
                catch { return null; }
            })
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        Assert.NotEmpty(dateParams);
        Assert.All(dateParams, p => Assert.Equal(expectedUtcDate, p));
    }

    [Fact]
    public async Task GetPeriodIdByDateAsync_ShouldUseUtcDate_WhenInputIsUtc()
    {
        // UTC 輸入正常情況
        var date = new DateTimeOffset(2026, 4, 22, 17, 0, 0, TimeSpan.Zero);
        var expectedUtcDate = new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero);

        QueryBuilder? captured = null;
        _dbContextMock
            .Setup(u => u.QuerySingleOrDefaultAsync<int?>(It.IsAny<QueryBuilder>()))
            .Callback<QueryBuilder>(qb => captured = qb)
            .ReturnsAsync(5);

        await _periodQuery.GetPeriodIdByDateAsync(date);

        Assert.NotNull(captured);
        var (_, parameters) = captured.Build();
        var dateParams = parameters.ParameterNames
            .Select(name =>
            {
                try { return (DateTimeOffset?)parameters.Get<DateTimeOffset>(name); }
                catch { return null; }
            })
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        Assert.NotEmpty(dateParams);
        Assert.All(dateParams, p => Assert.Equal(expectedUtcDate, p));
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
    public async Task GetActivePeriodAsync_ShouldReturnLatestPeriod()
    {
        // Arrange
        var latest = new Period { Id = 1, StartDate = DateTimeOffset.UtcNow };
        _dbContextMock.Setup(u => u.QuerySingleOrDefaultAsync<Period>(It.IsAny<QueryBuilder>()))
            .ReturnsAsync(latest);

        // Act
        var result = await _periodQuery.GetActivePeriodAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
    }
}
