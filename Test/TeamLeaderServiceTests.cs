using Application.DTOs;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Moq;
using Xunit;

namespace Test;

public class TeamLeaderServiceTests
{
    private readonly Mock<IBossRepository> _bossRepositoryMock = new();
    private readonly Mock<IPeriodQuery> _periodQueryMock = new();
    private readonly Mock<ITeamSlotRepository> _teamSlotRepositoryMock = new();
    private readonly Mock<ITeamSlotRequirementRepository> _requirementRepositoryMock = new();
    private readonly Mock<ITeamCandidateQuery> _candidateQueryMock = new();
    private readonly Mock<ITeamSlotCharacterRepository> _memberRepositoryMock = new();
    private readonly Mock<ICharacterQuery> _characterQueryMock = new();
    private readonly Mock<IRegistrationLock> _registrationLockMock = new();
    private readonly TeamLeaderService _service;

    public TeamLeaderServiceTests()
    {
        _service = new TeamLeaderService(
            _bossRepositoryMock.Object,
            _periodQueryMock.Object,
            _teamSlotRepositoryMock.Object,
            _requirementRepositoryMock.Object,
            _candidateQueryMock.Object,
            _memberRepositoryMock.Object,
            _characterQueryMock.Object,
            _registrationLockMock.Object,
            new Mock<Application.Interface.IOutbox>().Object,
            new Mock<Application.Queries.ITeamMembershipQuery>().Object);
    }

    private CreateTeamCommand ValidCommand() => new()
    {
        LeaderDiscordId = 999,
        BossId = 1,
        SlotDateTime = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero),
        Description = "楓葉祝福9",
        Requirements =
        [
            new CreateTeamRequirementDto
            {
                Count = 1,
                MinClearCount = 1,
                Jobs =
                [
                    new CreateTeamRequirementJobDto { Job = "箭神", MinAttackPower = 900 },
                    new CreateTeamRequirementJobDto { Job = "槍神", MinAttackPower = 1000 },
                ]
            }
        ]
    };

    [Fact]
    public async Task CreateTeamAsync_CreatesLeaderTeamAndRequirements_WhenValid()
    {
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(1))
            .ReturnsAsync(new Boss { Id = 1, Name = "王", RequireMembers = 6 });
        _periodQueryMock.Setup(p => p.GetPeriodIdByDateAsync(It.IsAny<DateTimeOffset>())).ReturnsAsync(5);
        _teamSlotRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<TeamSlot>())).ReturnsAsync(100);

        var id = await _service.CreateTeamAsync(ValidCommand());

        Assert.Equal(100, id);
        // 建的是 leader 隊，帶隊長 + 由日期解析的 PeriodId
        _teamSlotRepositoryMock.Verify(r => r.CreateAsync(It.Is<TeamSlot>(t =>
            t.Source == TeamSlotSource.Leader &&
            t.BossId == 1 &&
            t.PeriodId == 5 &&
            t.LeaderDiscordId == 999UL &&
            t.Description == "楓葉祝福9")), Times.Once);
        // 條件列連同其職業一起寫入
        _requirementRepositoryMock.Verify(r => r.CreateAsync(It.Is<TeamSlotRequirement>(rq =>
            rq.TeamSlotId == 100 && rq.Count == 1 && rq.MinClearCount == 1 && rq.Jobs.Count == 2)), Times.Once);
    }

    [Fact]
    public async Task CreateTeamAsync_ThrowsNotFound_WhenBossMissing()
    {
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((Boss?)null);

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(
            () => _service.CreateTeamAsync(ValidCommand()));
        _teamSlotRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<TeamSlot>()), Times.Never);
    }

    [Fact]
    public async Task CreateTeamAsync_ThrowsBusiness_WhenSlotTimeNotInAnyPeriod()
    {
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(1))
            .ReturnsAsync(new Boss { Id = 1, Name = "王", RequireMembers = 6 });
        _periodQueryMock.Setup(p => p.GetPeriodIdByDateAsync(It.IsAny<DateTimeOffset>())).ReturnsAsync(0); // 查無週期

        await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(
            () => _service.CreateTeamAsync(ValidCommand()));
        _teamSlotRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<TeamSlot>()), Times.Never);
    }
}
