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
    private readonly Mock<IOutbox> _outboxMock;
    private readonly SystemConfigService _service;

    public SystemConfigServiceTests()
    {
        var conn = new Mock<IDbConnection>().Object;
        _dbContextMock = new Mock<DbContext>(conn);
        _repoMock = new Mock<IRepository<SystemConfigDbModel>>();
        _dbContextMock.Setup(u => u.Repository<SystemConfigDbModel>()).Returns(_repoMock.Object);
        _outboxMock = new Mock<IOutbox>();
        _service = new SystemConfigService(_dbContextMock.Object, _outboxMock.Object);
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

    [Fact]
    public async Task UpdateAsync_發ConfigChanged事件到Outbox()
    {
        // Arrange
        _repoMock.Setup(r => r.GetAllAsync<SystemConfigDbModel>(null))
            .ReturnsAsync(new List<SystemConfigDbModel>());

        // Act
        await _service.UpdateAsync(new SystemConfig { LeaveRateWarnEnabled = true });

        // Assert：設定變更寫成 outbox 事件（與 UPDATE 同一交易 → commit 才生效、rollback 丟棄，
        // 那個原子性由整合測對真 DB 驗；此處單元只驗「有 enqueue、type 正確」）。
        _outboxMock.Verify(o => o.EnqueueAsync(OutboxEventType.ConfigChanged, It.IsAny<object>()), Times.Once);
    }
}
