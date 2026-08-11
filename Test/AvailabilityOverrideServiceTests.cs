using Application.DTOs;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class AvailabilityOverrideServiceTests
{
    private readonly Mock<IPlayerAvailabilityOverrideRepository> _repo = new();
    private readonly AvailabilityOverrideService _service;

    public AvailabilityOverrideServiceTests() => _service = new AvailabilityOverrideService(_repo.Object);

    [Fact]
    public async Task AddAsync_Creates_WhenValidWindow()
    {
        await _service.AddAsync(new AvailabilityOverrideCreateCommand
        {
            DiscordId = 999,
            Date = new DateOnly(2026, 4, 8),
            StartTime = new TimeOnly(19, 0),
            EndTime = new TimeOnly(22, 0),
            IsAvailable = true
        });

        _repo.Verify(r => r.CreateAsync(It.Is<PlayerAvailabilityOverride>(o =>
            o.DiscordId == 999UL && o.IsAvailable && o.StartTime == new TimeOnly(19, 0))), Times.Once);
    }

    [Fact]
    public async Task AddAsync_AllowsWholeDay_WhenEndIsMidnight()
    {
        await _service.AddAsync(new AvailabilityOverrideCreateCommand
        {
            DiscordId = 999,
            Date = new DateOnly(2026, 4, 8),
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(0, 0), // 整天
            IsAvailable = false
        });

        _repo.Verify(r => r.CreateAsync(It.IsAny<PlayerAvailabilityOverride>()), Times.Once);
    }

    [Fact]
    public async Task AddAsync_Throws_WhenEmptyWindow()
    {
        await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(() => _service.AddAsync(
            new AvailabilityOverrideCreateCommand
            {
                DiscordId = 999,
                Date = new DateOnly(2026, 4, 8),
                StartTime = new TimeOnly(20, 0),
                EndTime = new TimeOnly(19, 0) // 結束早於開始
            }));
        _repo.Verify(r => r.CreateAsync(It.IsAny<PlayerAvailabilityOverride>()), Times.Never);
    }

    [Fact]
    public async Task RemoveAsync_DelegatesToRepoWithOwnDiscordId()
    {
        await _service.RemoveAsync(999, 5);
        _repo.Verify(r => r.DeleteAsync(999UL, 5), Times.Once);
    }
}
