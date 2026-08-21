using Application.DTOs;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class LfgServiceTests
{
    private readonly Mock<ILfgIntentRepository> _repo = new();
    private readonly Mock<ICharacterQuery> _characterQuery = new();
    private readonly LfgService _service;

    public LfgServiceTests() => _service = new LfgService(_repo.Object, _characterQuery.Object);

    private static Character Char(string id) => new() { Id = id, DiscordId = 999, Name = "C", Job = "英雄", AttackPower = 900 };

    [Fact]
    public async Task PostAsync_Creates_WhenCharacterOwned()
    {
        _characterQuery.Setup(q => q.GetByDiscordIdAsync(999)).ReturnsAsync(new[] { Char("c1") });

        await _service.PostAsync(new LfgIntentCreateCommand { DiscordId = 999, CharacterId = "c1", BossId = 5 });

        _repo.Verify(r => r.CreateAsync(It.Is<LfgIntent>(i =>
            i.DiscordId == 999UL && i.CharacterId == "c1" && i.BossId == 5 && i.ExpiresAt > DateTimeOffset.UtcNow)), Times.Once);
    }

    [Fact]
    public async Task PostAsync_ThrowsNotFound_WhenCharacterNotOwned()
    {
        _characterQuery.Setup(q => q.GetByDiscordIdAsync(999)).ReturnsAsync(new[] { Char("c1") });

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(() =>
            _service.PostAsync(new LfgIntentCreateCommand { DiscordId = 999, CharacterId = "cX", BossId = 5 }));
        _repo.Verify(r => r.CreateAsync(It.IsAny<LfgIntent>()), Times.Never);
    }

    [Fact]
    public async Task PostAsync_ThrowsBusiness_WhenNoBoss()
    {
        // 無「任意王」：必須指定一隻王（BossId <= 0 → 4xx），且在擁有權檢查之前就擋。
        await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(() =>
            _service.PostAsync(new LfgIntentCreateCommand { DiscordId = 999, CharacterId = "c1" }));
        _repo.Verify(r => r.CreateAsync(It.IsAny<LfgIntent>()), Times.Never);
    }

    [Fact]
    public async Task CancelAsync_DelegatesWithOwnDiscordId()
    {
        await _service.CancelAsync(999, 7);
        _repo.Verify(r => r.DeleteAsync(999UL, 7), Times.Once);
    }
}
