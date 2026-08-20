using Application.Interface;
using Infrastructure.Dapper;
using System.Data;
using Domain.Entities;
using Infrastructure.Entities;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class SystemConfigServiceTests
{
    private readonly Mock<DbContext> _dbContextMock;
    private readonly Mock<IRepository<SystemConfigDbModel>> _repoMock;
    private readonly SystemConfigService _service;

    public SystemConfigServiceTests()
    {
        var conn = new Mock<IDbConnection>().Object;
        _dbContextMock = new Mock<DbContext>(conn);
        _repoMock = new Mock<IRepository<SystemConfigDbModel>>();
        _dbContextMock.Setup(u => u.Repository<SystemConfigDbModel>()).Returns(_repoMock.Object);
        _service = new SystemConfigService(_dbContextMock.Object);
    }

    [Fact]
    public async Task GetAsync_WhenNoConfigExists_ShouldReturnDefault()
    {
        // Arrange
        _repoMock.Setup(r => r.GetAllAsync<SystemConfigDbModel>(null))
            .ReturnsAsync(new List<SystemConfigDbModel>());

        // Act
        var result = await _service.GetAsync();

        // Assert（period-less：無截止欄位，退團率警示預設關）
        Assert.Equal(1, result.Id);
        Assert.False(result.LeaveRateWarnEnabled);
    }

    [Fact]
    public async Task GetAsync_WhenConfigExists_ShouldReturnConfig()
    {
        // Arrange
        var dbModels = new List<SystemConfigDbModel>
        {
            new SystemConfigDbModel { Id = 1, LeaveRateWarnEnabled = true, LeaveRateWindowMonths = 6, LeaveRateThreshold = 40, LeaveRateMinSample = 8 }
        };
        _repoMock.Setup(r => r.GetAllAsync<SystemConfigDbModel>(null))
            .ReturnsAsync(dbModels);

        // Act
        var result = await _service.GetAsync();

        // Assert
        Assert.True(result.LeaveRateWarnEnabled);
        Assert.Equal(6, result.LeaveRateWindowMonths);
        Assert.Equal(40, result.LeaveRateThreshold);
        Assert.Equal(8, result.LeaveRateMinSample);
    }

    [Fact]
    public async Task UpdateAsync_WhenNoExisting_ShouldInsert()
    {
        // Arrange
        _repoMock.Setup(r => r.GetAllAsync<SystemConfigDbModel>(null))
            .ReturnsAsync(new List<SystemConfigDbModel>());
        var config = new SystemConfig { LeaveRateWarnEnabled = true };

        // Act
        await _service.UpdateAsync(config);

        // Assert
        _repoMock.Verify(r => r.InsertAsync(It.IsAny<SystemConfigDbModel>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WhenExisting_ShouldUpdateLeaveRateFields()
    {
        // Arrange
        var existing = new SystemConfigDbModel { Id = 1, LeaveRateWarnEnabled = false, LeaveRateThreshold = 30 };

        _repoMock.Setup(r => r.GetAllAsync<SystemConfigDbModel>(null))
            .ReturnsAsync(new List<SystemConfigDbModel> { existing });

        var updateConfig = new SystemConfig { LeaveRateWarnEnabled = true, LeaveRateThreshold = 50 };

        // Act
        await _service.UpdateAsync(updateConfig);

        // Assert
        _repoMock.Verify(r => r.UpdateAsync(It.Is<SystemConfigDbModel>(m =>
            m.LeaveRateWarnEnabled && m.LeaveRateThreshold == 50)), Times.Once);
    }
}
