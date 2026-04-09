using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;
using Application.Interface;

namespace Test;

public class BossServiceTests
{
    private readonly Mock<IBossRepository> _bossRepositoryMock;
    private readonly BossService _bossService;

    public BossServiceTests()
    {
        _bossRepositoryMock = new Mock<IBossRepository>();

        _bossService = new BossService(_bossRepositoryMock.Object);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnBosses()
    {
        // Arrange
        var bosses = new List<Boss> { new Boss { Id = 1, Name = "Zakum" } };
        _bossRepositoryMock.Setup(r => r.GetAllAsync()).ReturnsAsync(bosses);

        // Act
        var result = await _bossService.GetAllAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("Zakum", result.First().Name);
    }

    [Fact]
    public async Task GetTemplatesByBossIdAsync_ShouldReturnTemplates()
    {
        // Arrange
        var templates = new List<BossTemplate> { new BossTemplate { Id = 1, BossId = 10 } };
        _bossRepositoryMock.Setup(r => r.GetTemplatesByBossIdAsync(10)).ReturnsAsync(templates);

        // Act
        var result = await _bossService.GetTemplatesByBossIdAsync(10);

        // Assert
        Assert.Single(result);
        Assert.Equal(10, result.First().BossId);
    }

    [Fact]
    public async Task GetTemplateByIdAsync_ShouldReturnTemplate()
    {
        // Arrange
        var template = new BossTemplate { Id = 1 };
        _bossRepositoryMock.Setup(r => r.GetTemplateByIdAsync(1)).ReturnsAsync(template);

        // Act
        var result = await _bossService.GetTemplateByIdAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
    }

    [Fact]
    public async Task CreateTemplateAsync_ShouldReturnId()
    {
        // Arrange
        var template = new BossTemplate { BossId = 1 };
        _bossRepositoryMock.Setup(r => r.CreateTemplateAsync(template)).ReturnsAsync(123);

        // Act
        var result = await _bossService.CreateTemplateAsync(template);

        // Assert
        Assert.Equal(123, result);
    }

    [Fact]
    public async Task UpdateTemplateAsync_ShouldReturnTrue()
    {
        // Arrange
        var template = new BossTemplate { Id = 1 };
        _bossRepositoryMock.Setup(r => r.UpdateTemplateAsync(template)).ReturnsAsync(true);

        // Act
        var result = await _bossService.UpdateTemplateAsync(template);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task DeleteTemplateAsync_ShouldReturnTrue()
    {
        // Arrange
        _bossRepositoryMock.Setup(r => r.DeleteTemplateAsync(1)).ReturnsAsync(true);

        // Act
        var result = await _bossService.DeleteTemplateAsync(1);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task DeleteBossAsync_ShouldCascadeDeleteTemplates()
    {
        // Arrange
        var templates = new List<BossTemplate>
        {
            new BossTemplate { Id = 10, BossId = 1 },
            new BossTemplate { Id = 20, BossId = 1 }
        };
        _bossRepositoryMock.Setup(r => r.GetTemplatesByBossIdAsync(1)).ReturnsAsync(templates);
        _bossRepositoryMock.Setup(r => r.DeleteTemplateAsync(It.IsAny<int>())).ReturnsAsync(true);
        _bossRepositoryMock.Setup(r => r.DeleteBossAsync(1)).ReturnsAsync(true);

        // Act
        var result = await _bossService.DeleteBossAsync(1);

        // Assert
        Assert.True(result);
        _bossRepositoryMock.Verify(r => r.GetTemplatesByBossIdAsync(1), Times.Once);
        _bossRepositoryMock.Verify(r => r.DeleteTemplateAsync(10), Times.Once);
        _bossRepositoryMock.Verify(r => r.DeleteTemplateAsync(20), Times.Once);
        _bossRepositoryMock.Verify(r => r.DeleteBossAsync(1), Times.Once);
    }

    [Fact]
    public async Task DeleteBossAsync_WithNoTemplates_ShouldDeleteBossOnly()
    {
        // Arrange
        _bossRepositoryMock.Setup(r => r.GetTemplatesByBossIdAsync(1)).ReturnsAsync(new List<BossTemplate>());
        _bossRepositoryMock.Setup(r => r.DeleteBossAsync(1)).ReturnsAsync(true);

        // Act
        var result = await _bossService.DeleteBossAsync(1);

        // Assert
        Assert.True(result);
        _bossRepositoryMock.Verify(r => r.DeleteTemplateAsync(It.IsAny<int>()), Times.Never);
        _bossRepositoryMock.Verify(r => r.DeleteBossAsync(1), Times.Once);
    }
}
