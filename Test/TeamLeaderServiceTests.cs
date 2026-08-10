using Application.DTOs;
using Application.Interface;
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
    private readonly Mock<ITeamMembershipQuery> _membershipQueryMock = new();
    private readonly Mock<ISystemConfigService> _systemConfigServiceMock = new();
    private readonly TeamLeaderService _service;

    public TeamLeaderServiceTests()
    {
        _systemConfigServiceMock.Setup(s => s.GetAsync()).ReturnsAsync(new Domain.Entities.SystemConfig());
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
            _membershipQueryMock.Object,
            _systemConfigServiceMock.Object);
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

    [Fact]
    public async Task GetLedTeamsAsync_ReturnsEmpty_WhenNoActivePeriod()
    {
        _periodQueryMock.Setup(p => p.GetActivePeriodIdAsync()).ReturnsAsync(0);

        var result = await _service.GetLedTeamsAsync(999);

        Assert.Empty(result);
        _membershipQueryMock.Verify(q => q.GetLedTeamsAsync(It.IsAny<ulong>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetLedTeamsAsync_QueriesByLeaderAndActivePeriod()
    {
        _periodQueryMock.Setup(p => p.GetActivePeriodIdAsync()).ReturnsAsync(7);
        var teams = new[] { new LedTeamDto { TeamSlotId = 100, BossName = "王", AppliedCount = 2 } };
        _membershipQueryMock.Setup(q => q.GetLedTeamsAsync(999, 7)).ReturnsAsync(teams);

        var result = await _service.GetLedTeamsAsync(999);

        Assert.Same(teams, result);
        _membershipQueryMock.Verify(q => q.GetLedTeamsAsync(999, 7), Times.Once);
    }

    private static TeamSlotCharacter ConfirmedMember() => new()
    {
        Id = 5,
        TeamSlotId = 10,
        DiscordId = 999,
        DiscordName = "X",
        Job = "英雄",
        Status = TeamSlotMemberStatus.Confirmed,
        Version = "v1"
    };

    [Fact]
    public async Task LeaveTeamAsync_LeavesConfirmedMember_WhenSelfConfirmed()
    {
        _memberRepositoryMock.Setup(r => r.GetConfirmedMemberAsync(10, 999UL)).ReturnsAsync(ConfirmedMember());
        _memberRepositoryMock.Setup(r => r.LeaveAsync(5, "v1")).ReturnsAsync(true);
        // team 回 null → 通知略過，測試聚焦退隊本身

        await _service.LeaveTeamAsync(10, 999);

        _memberRepositoryMock.Verify(r => r.LeaveAsync(5, "v1"), Times.Once);
    }

    [Fact]
    public async Task LeaveTeamAsync_ThrowsNotFound_WhenNotConfirmedMember()
    {
        _memberRepositoryMock.Setup(r => r.GetConfirmedMemberAsync(10, 999UL)).ReturnsAsync((TeamSlotCharacter?)null);

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(() => _service.LeaveTeamAsync(10, 999));
        _memberRepositoryMock.Verify(r => r.LeaveAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LeaveTeamAsync_ThrowsBusiness_WhenVersionConflict()
    {
        _memberRepositoryMock.Setup(r => r.GetConfirmedMemberAsync(10, 999UL)).ReturnsAsync(ConfirmedMember());
        _memberRepositoryMock.Setup(r => r.LeaveAsync(5, "v1")).ReturnsAsync(false); // xmin 對不上

        await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(() => _service.LeaveTeamAsync(10, 999));
    }

    // 團時間 2026-04-08 12:00Z = 週三 20:00 TPE（ISO weekday 3）；候選帶整天可用時段 + 符合需求。
    private void SetupCandidatesPipeline(CandidatePoolItem item)
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new TeamSlot
        {
            Id = 10,
            BossId = 1,
            SlotDateTime = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero)
        });
        _periodQueryMock.Setup(p => p.GetPeriodIdByDateAsync(It.IsAny<DateTimeOffset>())).ReturnsAsync(1);
        _periodQueryMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(new Period
        {
            Id = 1,
            StartDate = new DateTimeOffset(2026, 4, 6, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 4, 13, 0, 0, 0, TimeSpan.Zero)
        });
        _requirementRepositoryMock.Setup(r => r.GetByTeamSlotIdAsync(10)).ReturnsAsync(new[]
        {
            new TeamSlotRequirement { Count = 1, MinClearCount = 0, Jobs = [new TeamSlotRequirementJob { Job = "英雄", MinAttackPower = 0 }] }
        });
        _memberRepositoryMock.Setup(r => r.GetActiveMemberDiscordIdsAsync(10)).ReturnsAsync(new HashSet<ulong>());
        _candidateQueryMock.Setup(q => q.GetPoolAsync(1, 1)).ReturnsAsync(new[] { item });
    }

    private static CandidatePoolItem WarnCandidate() => new()
    {
        CharacterId = "c1",
        CharacterName = "C",
        DiscordId = 777,
        Job = "英雄",
        AttackPower = 900,
        BossClearCount = 0,
        Availabilities = [new PlayerAvailability { Weekday = 3, StartTime = new TimeOnly(0, 0), EndTime = new TimeOnly(0, 0) }]
    };

    [Fact]
    public async Task GetCandidatesAsync_SetsLeaveRateWarn_WhenEnabledAndHighRate()
    {
        SetupCandidatesPipeline(WarnCandidate());
        _systemConfigServiceMock.Setup(s => s.GetAsync())
            .ReturnsAsync(new Domain.Entities.SystemConfig { LeaveRateWarnEnabled = true, LeaveRateWindowMonths = 3, LeaveRateThreshold = 30, LeaveRateMinSample = 5 });
        _candidateQueryMock.Setup(q => q.GetHighLeaveRateDiscordIdsAsync(
                It.IsAny<IEnumerable<ulong>>(), It.IsAny<DateTimeOffset>(), 5, 30))
            .ReturnsAsync(new HashSet<ulong> { 777 });

        var result = (await _service.GetCandidatesAsync(10)).ToList();

        Assert.Single(result);
        Assert.True(result[0].LeaveRateWarn);
    }

    [Fact]
    public async Task GetCandidatesAsync_NoWarn_WhenConfigDisabled()
    {
        SetupCandidatesPipeline(WarnCandidate());
        // 預設 config（建構子設）LeaveRateWarnEnabled=false → 不查率、warn 一律 false

        var result = (await _service.GetCandidatesAsync(10)).ToList();

        Assert.Single(result);
        Assert.False(result[0].LeaveRateWarn);
        _candidateQueryMock.Verify(q => q.GetHighLeaveRateDiscordIdsAsync(
            It.IsAny<IEnumerable<ulong>>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProposeLeaderTransferAsync_SetsPending_WhenLeaderAndTargetConfirmed()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(ConfirmedMember()); // DiscordId=999, TeamSlotId=10, Confirmed

        await _service.ProposeLeaderTransferAsync(10, 5, 111);

        _teamSlotRepositoryMock.Verify(r => r.SetPendingLeaderAsync(10, 999UL), Times.Once);
    }

    [Fact]
    public async Task ProposeLeaderTransferAsync_ThrowsForbidden_WhenNotLeader()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111 });

        await Assert.ThrowsAsync<Application.Exceptions.ForbiddenException>(() => _service.ProposeLeaderTransferAsync(10, 5, 222));
        _teamSlotRepositoryMock.Verify(r => r.SetPendingLeaderAsync(It.IsAny<int>(), It.IsAny<ulong?>()), Times.Never);
    }

    [Fact]
    public async Task RespondLeaderTransferAsync_Completes_WhenAcceptByTarget()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, PendingLeaderDiscordId = 999, SlotDateTime = DateTimeOffset.UtcNow });

        await _service.RespondLeaderTransferAsync(10, 999, "accept");

        _teamSlotRepositoryMock.Verify(r => r.CompleteLeaderTransferAsync(10, 999UL), Times.Once);
    }

    [Fact]
    public async Task RespondLeaderTransferAsync_ThrowsForbidden_WhenNotPendingTarget()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, PendingLeaderDiscordId = 999 });

        await Assert.ThrowsAsync<Application.Exceptions.ForbiddenException>(() => _service.RespondLeaderTransferAsync(10, 888, "accept"));
        _teamSlotRepositoryMock.Verify(r => r.CompleteLeaderTransferAsync(It.IsAny<int>(), It.IsAny<ulong>()), Times.Never);
    }

    private static TeamSlotCharacter InvitedMember() => new()
    {
        Id = 5,
        TeamSlotId = 10,
        DiscordId = 999,
        DiscordName = "X",
        Job = "英雄",
        Status = TeamSlotMemberStatus.Invited,
        Version = "v1"
    };

    [Fact]
    public async Task AcceptInviteAsync_RevokesRemainingInvites_WhenTeamBecomesFull()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(InvitedMember());
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(1)).ReturnsAsync(new Boss { Id = 1, Name = "王", RequireMembers = 1 });
        // 定案前 0（<容量 → 通過把關）；定案後 1（>=容量 → 觸發撤銷）
        _memberRepositoryMock.SetupSequence(r => r.CountConfirmedAsync(10)).ReturnsAsync(0).ReturnsAsync(1);
        _memberRepositoryMock.Setup(r => r.UpdateStatusAsync(5, TeamSlotMemberStatus.Confirmed, "v1")).ReturnsAsync(true);
        _memberRepositoryMock.Setup(r => r.RevokePendingInvitesAsync(10)).ReturnsAsync(new ulong[] { 888 });

        await _service.AcceptInviteAsync(5, 999);

        _memberRepositoryMock.Verify(r => r.RevokePendingInvitesAsync(10), Times.Once);
    }

    [Fact]
    public async Task AcceptInviteAsync_DoesNotRevoke_WhenTeamNotFull()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(InvitedMember());
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(1)).ReturnsAsync(new Boss { Id = 1, Name = "王", RequireMembers = 6 });
        _memberRepositoryMock.Setup(r => r.CountConfirmedAsync(10)).ReturnsAsync(0); // 前後皆 0 < 6 → 未滿
        _memberRepositoryMock.Setup(r => r.UpdateStatusAsync(5, TeamSlotMemberStatus.Confirmed, "v1")).ReturnsAsync(true);

        await _service.AcceptInviteAsync(5, 999);

        _memberRepositoryMock.Verify(r => r.RevokePendingInvitesAsync(It.IsAny<int>()), Times.Never);
    }
}
