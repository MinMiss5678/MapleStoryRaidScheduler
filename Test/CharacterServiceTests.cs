using Application.DTOs;
using Application.Exceptions;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class CharacterServiceTests
{
    private readonly Mock<ICharacterRepository> _characterRepositoryMock;
    private readonly Mock<ICharacterQuery> _characterQueryMock;
    private readonly Mock<ICharacterRegisterRepository> _characterRegisterRepositoryMock;
    private readonly CharacterService _characterService;

    public CharacterServiceTests()
    {
        _characterRepositoryMock = new Mock<ICharacterRepository>();
        _characterQueryMock = new Mock<ICharacterQuery>();
        _characterRegisterRepositoryMock = new Mock<ICharacterRegisterRepository>();
        _characterService = new CharacterService(
            _characterRepositoryMock.Object,
            _characterQueryMock.Object,
            _characterRegisterRepositoryMock.Object);
    }

    [Fact]
    public async Task GetWithDiscordNameAsync_ShouldReturnDtos()
    {
        // Arrange
        ulong discordId = 12345;
        var dtos = new List<CharacterDto>
        {
            new CharacterDto { Id = "c1", Name = "Hero", Job = "Warrior" }
        };
        _characterQueryMock.Setup(q => q.GetWithDiscordNameAsync(discordId, null)).ReturnsAsync(dtos);

        // Act
        var result = await _characterService.GetWithDiscordNameAsync(discordId);

        // Assert
        Assert.Single(result);
        Assert.Equal("c1", result.First().Id);
    }

    [Fact]
    public async Task GetWithDiscordNameAsync_WithBossId_ShouldPassBossIdToQuery()
    {
        // Arrange
        ulong discordId = 12345;
        int bossId = 7;
        _characterQueryMock.Setup(q => q.GetWithDiscordNameAsync(discordId, bossId))
            .ReturnsAsync(new List<CharacterDto>());

        // Act
        await _characterService.GetWithDiscordNameAsync(discordId, bossId);

        // Assert
        _characterQueryMock.Verify(q => q.GetWithDiscordNameAsync(discordId, bossId), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ShouldReturnId()
    {
        // Arrange
        var character = new Character { Id = "c1", DiscordId = 12345, Name = "Hero", Job = "Warrior" };
        _characterRepositoryMock.Setup(r => r.CreateAsync(character)).ReturnsAsync(1);

        // Act
        var result = await _characterService.CreateAsync(character);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task UpdateAsync_ShouldComplete_WhenCharacterExists()
    {
        // Arrange
        var character = new Character { Id = "c1" };
        _characterRepositoryMock.Setup(r => r.UpdateAsync(character)).ReturnsAsync(1);

        // Act & Assert (no exception)
        await _characterService.UpdateAsync(character);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowNotFoundException_WhenCharacterNotFound()
    {
        // Arrange
        var character = new Character { Id = "missing" };
        _characterRepositoryMock.Setup(r => r.UpdateAsync(character)).ReturnsAsync(0);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _characterService.UpdateAsync(character));
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteRegistersThenCharacter()
    {
        // Arrange
        ulong discordId = 12345;
        string charId = "c1";
        _characterRepositoryMock.Setup(r => r.DeleteAsync(discordId, charId)).ReturnsAsync(1);

        // Act
        await _characterService.DeleteAsync(discordId, charId);

        // Assert
        _characterRegisterRepositoryMock.Verify(r => r.DeleteByCharacterIdAsync(charId), Times.Once);
        _characterRepositoryMock.Verify(r => r.DeleteAsync(discordId, charId), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrowNotFoundException_WhenCharacterNotFound()
    {
        // Arrange
        ulong discordId = 12345;
        string charId = "missing";
        _characterRepositoryMock.Setup(r => r.DeleteAsync(discordId, charId)).ReturnsAsync(0);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _characterService.DeleteAsync(discordId, charId));
    }
}
