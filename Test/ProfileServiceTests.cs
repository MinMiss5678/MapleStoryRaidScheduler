using Application.DTOs;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class ProfileServiceTests
{
    private readonly Mock<IPlayerAvailabilityStandingRepository> _standing = new();
    private readonly Mock<ICharacterRepository> _characterRepo = new();
    private readonly Mock<ICharacterQuery> _characterQuery = new();
    private readonly Mock<IPlayerRepository> _playerRepo = new();
    private readonly ProfileService _service;

    public ProfileServiceTests() => _service = new ProfileService(_standing.Object, _characterRepo.Object, _characterQuery.Object, _playerRepo.Object);

    private static Character Char(string id, bool seeking = false) =>
        new() { Id = id, DiscordId = 999, Name = "C" + id, Job = "英雄", AttackPower = 900, IsSeekingRaid = seeking };

    [Fact]
    public async Task SaveAsync_ReplacesStandingAndSetsOptIn_WhenOwned()
    {
        _characterQuery.Setup(q => q.GetByDiscordIdAsync(999)).ReturnsAsync(new[] { Char("c1"), Char("c2") });

        await _service.SaveAsync(new ProfileSaveCommand
        {
            DiscordId = 999,
            Availabilities = [new PlayerAvailabilityDto { Weekday = 3, StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(22, 0) }],
            SeekingCharacterIds = ["c1"]
        });

        _standing.Verify(r => r.DeleteByDiscordIdAsync(999), Times.Once);
        _standing.Verify(r => r.CreateAsync(It.Is<PlayerAvailability>(a => a.DiscordId == 999UL && a.Weekday == 3)), Times.Once);
        _characterRepo.Verify(r => r.SetSeekingRaidForDiscordAsync(999, It.Is<IReadOnlyCollection<string>>(ids => ids.Count == 1 && ids.Contains("c1"))), Times.Once);
        // 心跳：重設常設時段/參戰＝re-affirm → bump 該玩家新鮮度（plans/2026-09-01）。
        _playerRepo.Verify(p => p.BumpLastAffirmedAsync(999UL), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_ThrowsNotFound_WhenSeekingAlienCharacter()
    {
        _characterQuery.Setup(q => q.GetByDiscordIdAsync(999)).ReturnsAsync(new[] { Char("c1") });

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(() => _service.SaveAsync(new ProfileSaveCommand
        {
            DiscordId = 999,
            SeekingCharacterIds = ["cX"] // 非本人角色
        }));
        _standing.Verify(r => r.DeleteByDiscordIdAsync(It.IsAny<ulong>()), Times.Never);
    }

    [Fact]
    public async Task SaveAsync_ThrowsBusiness_WhenEmptyTimeWindow()
    {
        _characterQuery.Setup(q => q.GetByDiscordIdAsync(999)).ReturnsAsync(Array.Empty<Character>());

        await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(() => _service.SaveAsync(new ProfileSaveCommand
        {
            DiscordId = 999,
            Availabilities = [new PlayerAvailabilityDto { Weekday = 3, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(19, 0) }]
        }));
    }

    [Fact]
    public async Task GetAsync_MapsStandingAndCharactersWithSeekingFlag()
    {
        _standing.Setup(r => r.GetByDiscordIdAsync(999)).ReturnsAsync(new[]
        {
            new PlayerAvailability { Weekday = 3, StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(22, 0) }
        });
        _characterQuery.Setup(q => q.GetByDiscordIdAsync(999)).ReturnsAsync(new[] { Char("c1", seeking: true), Char("c2") });

        var dto = await _service.GetAsync(999);

        Assert.Single(dto.Availabilities);
        Assert.Equal(2, dto.Characters.Count);
        Assert.True(dto.Characters.Single(c => c.Id == "c1").IsSeekingRaid);
        Assert.False(dto.Characters.Single(c => c.Id == "c2").IsSeekingRaid);
    }

    // ── 階段二：新鮮度提醒 DM 按鈕動作 ──
    [Fact]
    public async Task ReaffirmFreshnessAsync_BumpsPlayer()
    {
        await _service.ReaffirmFreshnessAsync(777UL);
        _playerRepo.Verify(p => p.BumpLastAffirmedAsync(777UL), Times.Once);
    }

    [Fact]
    public async Task OptOutSeekingAsync_ClearsAllSeeking_ButKeepsAvailability()
    {
        await _service.OptOutSeekingAsync(777UL);
        // 空集合 → 全部角色 IsSeekingRaid=false（退出候選池）
        _characterRepo.Verify(r => r.SetSeekingRaidForDiscordAsync(777UL,
            It.Is<IReadOnlyCollection<string>>(ids => ids.Count == 0)), Times.Once);
        // 不刪常設時段（可逆）
        _standing.Verify(r => r.DeleteByDiscordIdAsync(It.IsAny<ulong>()), Times.Never);
    }
}
