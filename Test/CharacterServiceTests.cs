using Application.DTOs;
using Application.Exceptions;
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
    private readonly Mock<ICharacterBossClearRepository> _bossClearRepositoryMock;
    private readonly CharacterService _characterService;

    public CharacterServiceTests()
    {
        _characterRepositoryMock = new Mock<ICharacterRepository>();
        _characterQueryMock = new Mock<ICharacterQuery>();
        _bossClearRepositoryMock = new Mock<ICharacterBossClearRepository>();
        _characterService = new CharacterService(
            _characterRepositoryMock.Object,
            _characterQueryMock.Object,
            _bossClearRepositoryMock.Object);
    }

    private void SetupOwns(ulong discordId, params string[] charIds) =>
        _characterQueryMock.Setup(q => q.GetByDiscordIdAsync(discordId))
            .ReturnsAsync(charIds.Select(id => new Character
            {
                Id = id,
                DiscordId = discordId,
                Name = "N",
                Job = "英雄",
                AttackPower = 0
            }));

    [Fact]
    public async Task GetWithDiscordNameAsync_ShouldReturnDtos()
    {
        // Arrange
        ulong discordId = 12345;
        var dtos = new List<CharacterDto>
        {
            new CharacterDto { Id = "c1", Name = "Hero", Job = "Warrior", DiscordName = "" }
        };
        _characterQueryMock.Setup(q => q.GetWithDiscordNameAsync(discordId, null)).ReturnsAsync(dtos);

        // Act
        var result = await _characterService.GetWithDiscordNameAsync(discordId);

        // Assert
        Assert.Single(result);
        Assert.Equal("c1", result.First().Id);
    }

    [Fact]
    public async Task GetWithDiscordNameAsync_WithBossId_ShouldReturnQueryResultForThatBoss()
    {
        // Arrange：只對 bossId=7 設定回傳，若 service 傳錯 bossId 會拿到空集合 → 斷言失敗（順帶保證參數傳對）
        ulong discordId = 12345;
        int bossId = 7;
        var expected = new List<CharacterDto> { new() { Id = "cX", Name = "N", Job = "J", DiscordName = "" } };
        _characterQueryMock.Setup(q => q.GetWithDiscordNameAsync(discordId, bossId)).ReturnsAsync(expected);

        // Act
        var result = await _characterService.GetWithDiscordNameAsync(discordId, bossId);

        // Assert
        Assert.Single(result);
        Assert.Equal("cX", result.First().Id);
    }

    [Fact]
    public async Task CreateAsync_ShouldReturnId()
    {
        // Arrange
        var request = new CharacterRequest { Id = "c1", DiscordId = 12345, Name = "Hero", Job = "Warrior" };
        _characterRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<Character>())).ReturnsAsync(1);

        // Act
        var result = await _characterService.CreateAsync(request);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task UpdateAsync_ShouldComplete_WhenCharacterExists()
    {
        // Arrange
        var request = new CharacterRequest { Id = "c1" };
        _characterRepositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Character>())).ReturnsAsync(1);

        // Act & Assert (no exception)
        await _characterService.UpdateAsync(request);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowNotFoundException_WhenCharacterNotFound()
    {
        // Arrange
        var request = new CharacterRequest { Id = "missing" };
        _characterRepositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Character>())).ReturnsAsync(0);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _characterService.UpdateAsync(request));
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteBossClearsThenCharacter()
    {
        // Arrange
        ulong discordId = 12345;
        string charId = "c1";
        _characterRepositoryMock.Setup(r => r.DeleteAsync(discordId, charId)).ReturnsAsync(1);

        // Act
        await _characterService.DeleteAsync(discordId, charId);

        // Assert
        _bossClearRepositoryMock.Verify(r => r.DeleteByCharacterIdAsync(charId), Times.Once);
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

    [Fact]
    public async Task SaveBossClearsAsync_ShouldUpsertEachClear_WhenOwned()
    {
        // Arrange
        ulong discordId = 12345;
        string charId = "c1";
        SetupOwns(discordId, charId);
        var clears = new List<BossClearDto> { new() { BossId = 1, ClearCount = 3 }, new() { BossId = 2, ClearCount = 0 } };

        // Act
        await _characterService.SaveBossClearsAsync(discordId, charId, clears);

        // Assert：逐王 upsert
        _bossClearRepositoryMock.Verify(r => r.UpsertAsync(It.Is<CharacterBossClear>(
            c => c.CharacterId == charId && c.BossId == 1 && c.ClearCount == 3)), Times.Once);
        _bossClearRepositoryMock.Verify(r => r.UpsertAsync(It.Is<CharacterBossClear>(
            c => c.CharacterId == charId && c.BossId == 2 && c.ClearCount == 0)), Times.Once);
    }

    [Fact]
    public async Task SaveBossClearsAsync_ShouldThrowNotFound_WhenCharacterNotOwned()
    {
        // Arrange：登入者名下沒有這個角色 → 不得寫別人的通關數
        ulong discordId = 12345;
        SetupOwns(discordId, "someoneElse");
        var clears = new List<BossClearDto> { new() { BossId = 1, ClearCount = 3 } };

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _characterService.SaveBossClearsAsync(discordId, "c1", clears));
        _bossClearRepositoryMock.Verify(r => r.UpsertAsync(It.IsAny<CharacterBossClear>()), Times.Never);
    }

    [Fact]
    public async Task GetBossClearsAsync_ShouldReturnDtos_WhenOwned()
    {
        // Arrange
        ulong discordId = 12345;
        string charId = "c1";
        SetupOwns(discordId, charId);
        _bossClearRepositoryMock.Setup(r => r.GetByCharacterIdAsync(charId)).ReturnsAsync(new List<CharacterBossClear>
        {
            new() { CharacterId = charId, BossId = 1, ClearCount = 5 }
        });

        // Act
        var result = (await _characterService.GetBossClearsAsync(discordId, charId)).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(1, result[0].BossId);
        Assert.Equal(5, result[0].ClearCount);
    }
}
