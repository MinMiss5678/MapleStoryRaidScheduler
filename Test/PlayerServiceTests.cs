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
    public async Task CreateAsync_ShouldUpsertToRepo_RegardlessOfExistence()
    {
        // 現為 upsert：一律呼叫 repo（既有玩家更新 DiscordName＝公會暱稱刷新），不再 check-then-skip。
        var player = new Player { DiscordId = 12345, DiscordName = "Nick", Role = "user" };

        await _playerService.CreateAsync(player);

        _playerRepositoryMock.Verify(r => r.CreateAsync(player), Times.Once);
        _playerRepositoryMock.Verify(r => r.ExistAsync(It.IsAny<ulong>()), Times.Never); // 不再先查存在
    }

    [Fact]
    public async Task GetAsync_ShouldReturnPlayer_WhenExists()
    {
        // Arrange
        ulong discordId = 12345;
        var player = new Player { DiscordId = discordId, DiscordName = "TestPlayer", Role = "" };
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

}
