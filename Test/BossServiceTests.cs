using Application.DTOs;
using Application.Exceptions;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

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
    public async Task CreateBossAsync_ShouldReturnId()
    {
        // Arrange
        var request = new BossRequest { Name = "Horntail", RequireMembers = 6 };
        _bossRepositoryMock.Setup(r => r.CreateBossAsync(It.IsAny<Boss>())).ReturnsAsync(42);

        // Act
        var result = await _bossService.CreateBossAsync(request);

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task UpdateBossAsync_ShouldComplete_WhenBossExists()
    {
        // Arrange
        var request = new BossRequest { Name = "Horntail", RequireMembers = 6 };
        _bossRepositoryMock.Setup(r => r.UpdateBossAsync(It.IsAny<Boss>())).ReturnsAsync(true);

        // Act & Assert (no exception)
        await _bossService.UpdateBossAsync(1, request);
    }

    [Fact]
    public async Task UpdateBossAsync_ShouldThrowNotFoundException_WhenBossNotFound()
    {
        // Arrange
        var request = new BossRequest { Name = "Horntail", RequireMembers = 6 };
        _bossRepositoryMock.Setup(r => r.UpdateBossAsync(It.IsAny<Boss>())).ReturnsAsync(false);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _bossService.UpdateBossAsync(99, request));
    }

    [Fact]
    public async Task DeleteBossAsync_ShouldDeleteBoss()
    {
        // Arrange
        _bossRepositoryMock.Setup(r => r.DeleteBossAsync(1)).ReturnsAsync(true);

        // Act
        await _bossService.DeleteBossAsync(1);

        // Assert
        _bossRepositoryMock.Verify(r => r.DeleteBossAsync(1), Times.Once);
    }

    [Fact]
    public async Task DeleteBossAsync_ShouldThrowNotFoundException_WhenBossNotFound()
    {
        // Arrange
        _bossRepositoryMock.Setup(r => r.DeleteBossAsync(99)).ReturnsAsync(false);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _bossService.DeleteBossAsync(99));
    }
}
