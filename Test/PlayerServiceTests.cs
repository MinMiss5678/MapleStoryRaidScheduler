using Application.Interface;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class PlayerServiceTests
{
    private readonly Mock<IPlayerRepository> _playerRepositoryMock;
    private readonly PlayerService _playerService;

    public PlayerServiceTests()
    {
        _playerRepositoryMock = new Mock<IPlayerRepository>();
        _playerService = new PlayerService(_playerRepositoryMock.Object);
    }

    [Fact]
    public async Task CreateAsync_ShouldCreatePlayer_WhenNotExists()
    {
        // Arrange
        var player = new Player { DiscordId = 12345, DiscordName = "TestPlayer" };
        _playerRepositoryMock.Setup(r => r.ExistAsync(player.DiscordId)).ReturnsAsync(false);

        // Act
        await _playerService.CreateAsync(player);

        // Assert
        _playerRepositoryMock.Verify(r => r.CreateAsync(player), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ShouldSkipCreation_WhenPlayerAlreadyExists()
    {
        // Arrange
        var player = new Player { DiscordId = 12345 };
        _playerRepositoryMock.Setup(r => r.ExistAsync(player.DiscordId)).ReturnsAsync(true);

        // Act
        await _playerService.CreateAsync(player);

        // Assert
        _playerRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<Player>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnPlayer_WhenExists()
    {
        // Arrange
        ulong discordId = 12345;
        var player = new Player { DiscordId = discordId, DiscordName = "TestPlayer" };
        _playerRepositoryMock.Setup(r => r.GetAsync(discordId)).ReturnsAsync(player);

        // Act
        var result = await _playerService.GetAsync(discordId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(discordId, result.DiscordId);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        _playerRepositoryMock.Setup(r => r.GetAsync(99999UL)).ReturnsAsync((Player?)null);

        // Act
        var result = await _playerService.GetAsync(99999UL);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateRoleAsync_ShouldCallRepository()
    {
        // Arrange
        ulong discordId = 12345;
        string role = "admin";

        // Act
        await _playerService.UpdateRoleAsync(discordId, role);

        // Assert
        _playerRepositoryMock.Verify(r => r.UpdateRoleAsync(discordId, role), Times.Once);
    }
}
