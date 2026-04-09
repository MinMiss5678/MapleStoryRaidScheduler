using Application.Events;
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
    private readonly ConfigChangeNotifier _notifier;
    private readonly SystemConfigService _service;

    public SystemConfigServiceTests()
    {
        var conn = new Mock<IDbConnection>().Object;
        _dbContextMock = new Mock<DbContext>(conn);
        _repoMock = new Mock<IRepository<SystemConfigDbModel>>();
        _dbContextMock.Setup(u => u.Repository<SystemConfigDbModel>()).Returns(_repoMock.Object);
        _notifier = new ConfigChangeNotifier();
        _service = new SystemConfigService(_dbContextMock.Object, _notifier);
    }

    [Fact]
    public async Task GetAsync_WhenNoConfigExists_ShouldReturnDefault()
    {
        // Arrange
        _repoMock.Setup(r => r.GetAllAsync<SystemConfigDbModel>(null))
            .ReturnsAsync(new List<SystemConfigDbModel>());

        // Act
        var result = await _service.GetAsync();

        // Assert
        Assert.Equal(DayOfWeek.Wednesday, result.DeadlineDayOfWeek);
        Assert.False(result.IsDeadlineNotified);
    }

    [Fact]
    public async Task GetAsync_WhenConfigExists_ShouldReturnConfig()
    {
        // Arrange
        var dbModels = new List<SystemConfigDbModel>
        {
            new SystemConfigDbModel { Id = 1, DeadlineDayOfWeek = (int)DayOfWeek.Thursday, DeadlineTime = new TimeSpan(12, 0, 0), IsDeadlineNotified = true }
        };
        _repoMock.Setup(r => r.GetAllAsync<SystemConfigDbModel>(null))
            .ReturnsAsync(dbModels);

        // Act
        var result = await _service.GetAsync();

        // Assert
        Assert.Equal(DayOfWeek.Thursday, result.DeadlineDayOfWeek);
        Assert.Equal(new TimeSpan(12, 0, 0), result.DeadlineTime);
        Assert.True(result.IsDeadlineNotified);
    }

    [Fact]
    public async Task UpdateAsync_WhenNoExisting_ShouldInsert()
    {
        // Arrange
        _repoMock.Setup(r => r.GetAllAsync<SystemConfigDbModel>(null))
            .ReturnsAsync(new List<SystemConfigDbModel>());
        var config = new SystemConfig { DeadlineDayOfWeek = DayOfWeek.Monday };

        // Act
        await _service.UpdateAsync(config);

        // Assert
        _repoMock.Verify(r => r.InsertAsync(It.IsAny<SystemConfigDbModel>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WhenDeadlineChanges_ShouldResetNotification()
    {
        // Arrange
        var existing = new SystemConfigDbModel { Id = 1, DeadlineDayOfWeek = (int)DayOfWeek.Monday, DeadlineTime = new TimeSpan(10, 0, 0), IsDeadlineNotified = true };
        
        _repoMock.Setup(r => r.GetAllAsync<SystemConfigDbModel>(null))
            .ReturnsAsync(new List<SystemConfigDbModel> { existing });
        
        var updateConfig = new SystemConfig { DeadlineDayOfWeek = DayOfWeek.Tuesday, DeadlineTime = new TimeSpan(10, 0, 0), IsDeadlineNotified = true };

        // Act
        await _service.UpdateAsync(updateConfig);

        // Assert
        _repoMock.Verify(r => r.UpdateAsync(It.Is<SystemConfigDbModel>(m => 
            m.DeadlineDayOfWeek == (int)DayOfWeek.Tuesday && m.IsDeadlineNotified == false)), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ShouldInvokeOnConfigUpdated()
    {
        // Arrange
        bool eventInvoked = false;
        _notifier.OnChanged += () => eventInvoked = true;
        
        _repoMock.Setup(r => r.GetAllAsync<SystemConfigDbModel>(null))
            .ReturnsAsync(new List<SystemConfigDbModel>());
        
        var config = new SystemConfig { DeadlineDayOfWeek = DayOfWeek.Wednesday };

        // Act
        await _service.UpdateAsync(config);

        // Assert
        Assert.True(eventInvoked);
    }
}
