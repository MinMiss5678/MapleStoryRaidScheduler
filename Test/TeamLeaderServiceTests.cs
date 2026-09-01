using Application.DTOs;
using Application.Events;
using Application.Interface;
using Application.Options;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Test;

public class TeamLeaderServiceTests
{
    private readonly Mock<IBossRepository> _bossRepositoryMock = new();
    private readonly Mock<ITeamSlotRepository> _teamSlotRepositoryMock = new();
    private readonly Mock<ITeamSlotRequirementRepository> _requirementRepositoryMock = new();
    private readonly Mock<ITeamCandidateQuery> _candidateQueryMock = new();
    private readonly Mock<ITeamSlotCharacterRepository> _memberRepositoryMock = new();
    private readonly Mock<ICharacterQuery> _characterQueryMock = new();
    private readonly Mock<ITeamSlotEditLock> _registrationLockMock = new();
    private readonly Mock<ITeamMembershipQuery> _membershipQueryMock = new();
    private readonly Mock<ISystemConfigService> _systemConfigServiceMock = new();
    private readonly Mock<Application.Interface.IOutbox> _outboxMock = new();
    private readonly TeamLeaderService _service;

    public TeamLeaderServiceTests()
    {
        _systemConfigServiceMock.Setup(s => s.GetAsync()).ReturnsAsync(new Domain.Entities.SystemConfig());
        _service = new TeamLeaderService(
            _bossRepositoryMock.Object,
            _teamSlotRepositoryMock.Object,
            _requirementRepositoryMock.Object,
            _candidateQueryMock.Object,
            _memberRepositoryMock.Object,
            _characterQueryMock.Object,
            _registrationLockMock.Object,
            _outboxMock.Object,
            _membershipQueryMock.Object,
            _systemConfigServiceMock.Object,
            _lfgIntentRepositoryMock.Object,
            _playerRepositoryMock.Object,
            Options.Create(new AppOptions { AppUrl = "https://test.local" }));
    }

    private readonly Mock<ILfgIntentRepository> _lfgIntentRepositoryMock = new();
    private readonly Mock<IPlayerRepository> _playerRepositoryMock = new();

    private CreateTeamCommand ValidCommand() => new()
    {
        LeaderDiscordId = 999,
        BossId = 1,
        // period-less（4d）：CreateTeam 只驗「排程時間不得早於現在」→ 用未來時間
        SlotDateTime = DateTimeOffset.UtcNow.AddDays(3),
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
        _teamSlotRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<TeamSlot>())).ReturnsAsync(100);

        var id = await _service.CreateTeamAsync(ValidCommand());

        Assert.Equal(100, id);
        // 建的是 leader 隊，帶隊長（period-less：不再有 PeriodId）
        _teamSlotRepositoryMock.Verify(r => r.CreateAsync(It.Is<TeamSlot>(t =>
            t.Source == TeamSlotSource.Leader &&
            t.BossId == 1 &&
            t.LeaderDiscordId == 999UL &&
            t.Description == "楓葉祝福9")), Times.Once);
        // 條件列連同其職業一起寫入
        _requirementRepositoryMock.Verify(r => r.CreateAsync(It.Is<TeamSlotRequirement>(rq =>
            rq.TeamSlotId == 100 && rq.Count == 1 && rq.MinClearCount == 1 && rq.Jobs.Count == 2)), Times.Once);
        // ValidCommand 無 LeaderCharacterId → 只揪人、不把隊長排進去
        _memberRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<TeamSlotCharacter>()), Times.Never);
    }

    [Fact]
    public async Task CreateTeamAsync_EnrollsLeaderAsConfirmed_WhenLeaderCharacterProvided()
    {
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(1))
            .ReturnsAsync(new Boss { Id = 1, Name = "王", RequireMembers = 6 });
        _teamSlotRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<TeamSlot>())).ReturnsAsync(100);
        _characterQueryMock.Setup(q => q.GetByIdAsync("myChar"))
            .ReturnsAsync(new Character { Id = "myChar", DiscordId = 999, Name = "隊長角色", Job = "英雄", AttackPower = 1500 });
        var cmd = ValidCommand();
        cmd.LeaderCharacterId = "myChar";

        await _service.CreateTeamAsync(cmd);

        // 隊長帶自己下去打 → 佔 1 位、自動 Confirmed
        _memberRepositoryMock.Verify(r => r.CreateAsync(It.Is<TeamSlotCharacter>(m =>
            m.TeamSlotId == 100 && m.DiscordId == 999UL && m.CharacterId == "myChar"
            && m.Status == TeamSlotMemberStatus.Confirmed)), Times.Once);
    }

    [Fact]
    public async Task CreateTeamAsync_ThrowsNotFound_WhenLeaderCharacterNotOwned()
    {
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(1))
            .ReturnsAsync(new Boss { Id = 1, Name = "王", RequireMembers = 6 });
        _teamSlotRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<TeamSlot>())).ReturnsAsync(100);
        // 角色屬於別人（888），不是隊長 999
        _characterQueryMock.Setup(q => q.GetByIdAsync("notMine"))
            .ReturnsAsync(new Character { Id = "notMine", DiscordId = 888, Name = "X", Job = "英雄", AttackPower = 0 });
        var cmd = ValidCommand();
        cmd.LeaderCharacterId = "notMine";

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(() => _service.CreateTeamAsync(cmd));
        _memberRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<TeamSlotCharacter>()), Times.Never);
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
    public async Task CreateTeamAsync_ThrowsBusiness_WhenScheduledSlotTimeInPast()
    {
        // period-less（4d）：排程團的 SlotDateTime 早於現在 → 擋（過去時段無意義）
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(1))
            .ReturnsAsync(new Boss { Id = 1, Name = "王", RequireMembers = 6 });
        var pastCommand = ValidCommand();
        pastCommand.SlotDateTime = DateTimeOffset.UtcNow.AddDays(-1);

        await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(
            () => _service.CreateTeamAsync(pastCommand));
        _teamSlotRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<TeamSlot>()), Times.Never);
    }

    [Fact]
    public async Task DeleteTeamAsync_DeletesAndNotifiesActiveMembers_WhenLeaderOwns()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 999, SlotDateTime = DateTimeOffset.UtcNow });
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(1)).ReturnsAsync(new Boss { Id = 1, Name = "王", RequireMembers = 6 });
        // active 含隊長自己（999，帶自己角色下去打）+ 兩位成員
        _memberRepositoryMock.Setup(r => r.GetActiveMemberDiscordIdsAsync(10))
            .ReturnsAsync(new HashSet<ulong> { 999, 101, 102 });

        await _service.DeleteTeamAsync(10, leaderDiscordId: 999);

        _teamSlotRepositoryMock.Verify(r => r.DeleteAsync(10), Times.Once);
        // 只通知非隊長的成員（排除按解散的隊長本人）→ 2 則，不是 3
        _outboxMock.Verify(o => o.EnqueueAsync(OutboxEventType.TeamNotification, It.IsAny<object>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Notification_AppendsAppUrlLink_WhenAppUrlConfigured()
    {
        // 建構子注入 AppOptions.AppUrl = "https://test.local"（見 ctor）→ NotifyAsync 應把它接在訊息末尾。
        object? captured = null;
        _outboxMock.Setup(o => o.EnqueueAsync(OutboxEventType.TeamNotification, It.IsAny<object>()))
            .Callback<string, object>((_, evt) => captured = evt);

        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 999, SlotDateTime = DateTimeOffset.UtcNow });
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(1)).ReturnsAsync(new Boss { Id = 1, Name = "王", RequireMembers = 6 });
        _memberRepositoryMock.Setup(r => r.GetActiveMemberDiscordIdsAsync(10))
            .ReturnsAsync(new HashSet<ulong> { 101 });

        await _service.DeleteTeamAsync(10, leaderDiscordId: 999);

        var evt = Assert.IsType<TeamNotificationEvent>(captured);
        Assert.Contains("https://test.local", evt.Message);
    }

    [Fact]
    public async Task DeleteTeamAsync_ThrowsForbidden_WhenNotLeader()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 888, SlotDateTime = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<Application.Exceptions.ForbiddenException>(
            () => _service.DeleteTeamAsync(10, leaderDiscordId: 999));
        _teamSlotRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteTeamAsync_ThrowsNotFound_WhenTeamMissing()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((TeamSlot?)null);

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(
            () => _service.DeleteTeamAsync(10, leaderDiscordId: 999));
        _teamSlotRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<int>()), Times.Never);
    }

    // ---- 招募缺口（GetRecruitmentGapAsync）逐列貪婪配對 ----

    private static OpenTeamRequirementDto Req(int count, params string[] jobs) => new()
    {
        Count = count,
        Jobs = jobs.Select(j => new OpenTeamRequirementJobDto { Job = j }).ToList()
    };

    private void SetupGap(int teamSlotId, IEnumerable<OpenTeamRequirementDto> reqs, IEnumerable<string> confirmedJobs)
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId))
            .ReturnsAsync(new TeamSlot { Id = teamSlotId, BossId = 1, LeaderDiscordId = 999, SlotDateTime = DateTimeOffset.UtcNow });
        _membershipQueryMock.Setup(q => q.GetRequirementsAsync(teamSlotId)).ReturnsAsync(reqs);
        _membershipQueryMock.Setup(q => q.GetConfirmedJobsAsync(teamSlotId)).ReturnsAsync(confirmedJobs);
    }

    [Fact]
    public async Task GetRecruitmentGapAsync_ReturnsPerRowShortfall()
    {
        SetupGap(10, [Req(2, "主教"), Req(1, "夜使者")], ["主教"]);

        var gap = (await _service.GetRecruitmentGapAsync(10, leaderDiscordId: 999)).ToList();

        Assert.Equal(1, gap.Single(g => g.Jobs.Contains("主教")).Remaining);   // 要 2 只有 1 → 缺 1
        Assert.Equal(1, gap.Single(g => g.Jobs.Contains("夜使者")).Remaining); // 要 1 有 0 → 缺 1
    }

    [Fact]
    public async Task GetRecruitmentGapAsync_FillsSpecificRowsBeforeUnlimited()
    {
        // 限定職業列先配，剩下的人才補「不限職業」列 → 兩列都滿足
        SetupGap(10, [Req(1, "主教"), Req(1)], ["主教", "夜使者"]);

        var gap = await _service.GetRecruitmentGapAsync(10, leaderDiscordId: 999);

        Assert.All(gap, g => Assert.Equal(0, g.Remaining));
    }

    [Fact]
    public async Task GetRecruitmentGapAsync_ClampsRemainingAtZero_WhenOverfilled()
    {
        SetupGap(10, [Req(1, "主教")], ["主教", "主教"]);

        var gap = await _service.GetRecruitmentGapAsync(10, leaderDiscordId: 999);

        Assert.Equal(0, gap.Single().Remaining); // 不會變負數
    }

    [Fact]
    public async Task GetRecruitmentGapAsync_ThrowsForbidden_WhenNotLeader()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 888, SlotDateTime = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<Application.Exceptions.ForbiddenException>(
            () => _service.GetRecruitmentGapAsync(10, leaderDiscordId: 999));
    }

    // ---- 隊伍組成（GetTeamMembersAsync）授權：成員/隊長可看、外人擋 ----

    private void SetupMembers(int teamSlotId, ulong leader)
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(teamSlotId))
            .ReturnsAsync(new TeamSlot { Id = teamSlotId, BossId = 1, LeaderDiscordId = leader, SlotDateTime = DateTimeOffset.UtcNow });
        _membershipQueryMock.Setup(q => q.GetConfirmedMembersAsync(teamSlotId))
            .ReturnsAsync([new TeamMemberDto { CharacterName = "小飛", Job = "箭神", IsLeader = true }]);
    }

    [Fact]
    public async Task GetTeamMembersAsync_ReturnsComposition_WhenRequesterIsConfirmedMember()
    {
        SetupMembers(10, leader: 999);
        _memberRepositoryMock.Setup(r => r.GetConfirmedMemberAsync(10, 555UL))
            .ReturnsAsync(new TeamSlotCharacter { TeamSlotId = 10, DiscordId = 555, DiscordName = "", Job = "箭神", Status = TeamSlotMemberStatus.Confirmed });

        var members = await _service.GetTeamMembersAsync(10, requesterDiscordId: 555);

        Assert.Single(members);
    }

    [Fact]
    public async Task GetTeamMembersAsync_ReturnsComposition_WhenRequesterIsLeaderEvenIfNotConfirmed()
    {
        SetupMembers(10, leader: 999);
        _memberRepositoryMock.Setup(r => r.GetConfirmedMemberAsync(10, 999UL)).ReturnsAsync((TeamSlotCharacter?)null);

        var members = await _service.GetTeamMembersAsync(10, requesterDiscordId: 999);

        Assert.Single(members);
    }

    [Fact]
    public async Task GetTeamMembersAsync_ThrowsForbidden_WhenRequesterNotMemberNorLeader()
    {
        SetupMembers(10, leader: 999);
        _memberRepositoryMock.Setup(r => r.GetConfirmedMemberAsync(10, 777UL)).ReturnsAsync((TeamSlotCharacter?)null);

        await Assert.ThrowsAsync<Application.Exceptions.ForbiddenException>(
            () => _service.GetTeamMembersAsync(10, requesterDiscordId: 777));
    }

    [Fact]
    public async Task ApplyAsync_ThrowsBusiness_WhenAlreadyActiveInTeam()
    {
        // 已在此隊 active（Confirmed/Invited/Applied）→ 不得再申請（uq_tsc_active_membership 擋不住已 Confirmed 的情況）
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, SlotDateTime = DateTimeOffset.UtcNow });
        _characterQueryMock.Setup(q => q.GetByIdAsync("c1"))
            .ReturnsAsync(new Character { Id = "c1", DiscordId = 101, Name = "C", Job = "箭神", AttackPower = 900 });
        _memberRepositoryMock.Setup(r => r.GetActiveMemberDiscordIdsAsync(10))
            .ReturnsAsync(new HashSet<ulong> { 101 }); // 101 已在此隊 active

        await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(
            () => _service.ApplyAsync(10, "c1", applicantDiscordId: 101));
        _memberRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<TeamSlotCharacter>()), Times.Never);
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
        _requirementRepositoryMock.Setup(r => r.GetByTeamSlotIdAsync(10)).ReturnsAsync(new[]
        {
            new TeamSlotRequirement { Count = 1, MinClearCount = 0, Jobs = [new TeamSlotRequirementJob { Job = "英雄", MinAttackPower = 0 }] }
        });
        _memberRepositoryMock.Setup(r => r.GetActiveMemberDiscordIdsAsync(10)).ReturnsAsync(new HashSet<ulong>());
        _memberRepositoryMock.Setup(r => r.GetConfirmedDiscordIdsAtAsync(It.IsAny<DateTimeOffset>())).ReturnsAsync(new HashSet<ulong>());
        _candidateQueryMock.Setup(q => q.GetOverridesForDateAsync(It.IsAny<DateOnly>())).ReturnsAsync(Array.Empty<AvailabilityOverrideItem>());
        _candidateQueryMock.Setup(q => q.GetPoolAsync(1, It.IsAny<DateTimeOffset>())).ReturnsAsync(new[] { item });
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

    private static CandidatePoolItem PrefCandidate(string id, ulong discordId, bool prefersThis, bool hasAnyPref) => new()
    {
        CharacterId = id,
        CharacterName = id,
        DiscordId = discordId,
        Job = "英雄",
        AttackPower = 900,
        BossClearCount = 0,
        PrefersThisBoss = prefersThis,
        HasAnyPreference = hasAnyPref,
        Availabilities = [new PlayerAvailability { Weekday = 3, StartTime = new TimeOnly(0, 0), EndTime = new TimeOnly(0, 0) }]
    };

    [Fact]
    public async Task GetCandidatesAsync_SortsByPreference_ThisBossFirst_NoPrefNeutral_OtherLast()
    {
        // 三層軟訊號：偏好本王 → 沒設偏好(中性) → 設了但不含本王(殿後)；皆未被排除。
        var prefersOther = PrefCandidate("cOther", 701, prefersThis: false, hasAnyPref: true);
        var noPref = PrefCandidate("cNone", 702, prefersThis: false, hasAnyPref: false);
        var prefersThis = PrefCandidate("cThis", 703, prefersThis: true, hasAnyPref: true);
        SetupCandidatesPipeline(prefersThis);
        // 輸入故意亂序 → 驗證是後端排的
        _candidateQueryMock.Setup(q => q.GetPoolAsync(1, It.IsAny<DateTimeOffset>())).ReturnsAsync(new[] { prefersOther, noPref, prefersThis });

        var result = (await _service.GetCandidatesAsync(10)).ToList();

        Assert.Equal(new[] { "cThis", "cNone", "cOther" }, result.Select(r => r.CharacterId));
        Assert.True(result[0].PrefersThisBoss);
        Assert.False(result[2].PrefersThisBoss);
    }

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

    [Fact]
    public async Task InviteMemberAsync_ThrowsBusiness_WhenTeamFull()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(1)).ReturnsAsync(new Boss { Id = 1, Name = "王", RequireMembers = 1 });
        _memberRepositoryMock.Setup(r => r.CountConfirmedAsync(10)).ReturnsAsync(1); // 已滿（Confirmed 達容量）

        await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(() => _service.InviteMemberAsync(10, "cX", 111));
        _memberRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<TeamSlotCharacter>()), Times.Never);
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
        _memberRepositoryMock.Setup(r => r.RevokePendingInvitesAsync(10)).ReturnsAsync(new[] { new RevokedInvite(888, null) });

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

    // composition-quota：需求[英雄1]+[黑騎士1]、容量2（未指定池0）；已 1 英雄 Confirmed → 第 2 英雄接受被擋。
    [Fact]
    public async Task AcceptInviteAsync_BlocksWhenJobQuotaFull()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(InvitedMember()); // 英雄
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(1)).ReturnsAsync(new Boss { Id = 1, Name = "王", RequireMembers = 2 });
        _memberRepositoryMock.Setup(r => r.CountConfirmedAsync(10)).ReturnsAsync(1); // 1 < 2 → 過容量把關
        _requirementRepositoryMock.Setup(r => r.GetByTeamSlotIdAsync(10)).ReturnsAsync(new[]
        {
            new TeamSlotRequirement { Count = 1, Jobs = [new TeamSlotRequirementJob { Job = "英雄" }] },
            new TeamSlotRequirement { Count = 1, Jobs = [new TeamSlotRequirementJob { Job = "黑騎士" }] }
        });
        _membershipQueryMock.Setup(q => q.GetConfirmedJobsAsync(10)).ReturnsAsync(["英雄"]); // 英雄名額已被佔

        var ex = await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(() => _service.AcceptInviteAsync(5, 999));
        Assert.Contains("職業名額", ex.Message);
        _memberRepositoryMock.Verify(r => r.UpdateStatusAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // composition-quota：需求[英雄1]+[黑騎士1]、容量2；本英雄接受後英雄名額滿（隊未滿）→ 撤同職業其餘 pending。
    [Fact]
    public async Task AcceptInviteAsync_RevokesSameJobPending_WhenJobQuotaBecomesFull()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(InvitedMember()); // 英雄
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(1)).ReturnsAsync(new Boss { Id = 1, Name = "王", RequireMembers = 2 });
        _memberRepositoryMock.SetupSequence(r => r.CountConfirmedAsync(10)).ReturnsAsync(0).ReturnsAsync(1); // 前0後1（<2 未滿）
        _requirementRepositoryMock.Setup(r => r.GetByTeamSlotIdAsync(10)).ReturnsAsync(new[]
        {
            new TeamSlotRequirement { Count = 1, Jobs = [new TeamSlotRequirementJob { Job = "英雄" }] },
            new TeamSlotRequirement { Count = 1, Jobs = [new TeamSlotRequirementJob { Job = "黑騎士" }] }
        });
        // 定案前[]（過硬篩）；定案後[英雄]（判斷 pending 英雄不可行）
        _membershipQueryMock.SetupSequence(q => q.GetConfirmedJobsAsync(10)).ReturnsAsync([]).ReturnsAsync(["英雄"]);
        _membershipQueryMock.Setup(q => q.GetPendingInviteJobsAsync(10)).ReturnsAsync(["英雄"]);
        _memberRepositoryMock.Setup(r => r.UpdateStatusAsync(5, TeamSlotMemberStatus.Confirmed, "v1")).ReturnsAsync(true);
        _memberRepositoryMock.Setup(r => r.RevokePendingInvitesByJobsAsync(10, It.Is<IReadOnlyCollection<string>>(j => j.Contains("英雄"))))
            .ReturnsAsync(new[] { new RevokedInvite(888, null) });

        await _service.AcceptInviteAsync(5, 999);

        _memberRepositoryMock.Verify(r => r.RevokePendingInvitesByJobsAsync(10, It.Is<IReadOnlyCollection<string>>(j => j.Contains("英雄"))), Times.Once);
        _memberRepositoryMock.Verify(r => r.RevokePendingInvitesAsync(It.IsAny<int>()), Times.Never); // 未滿 → 不走全撤
    }

    // ── 候選過濾行為邊界（殺變異：Any→All / && / >= / == 存活）──
    private void SetRequirement(string job, int minAttack, int minClear)
        => _requirementRepositoryMock.Setup(r => r.GetByTeamSlotIdAsync(10)).ReturnsAsync(new[]
        {
            new TeamSlotRequirement { Count = 1, MinClearCount = minClear, Jobs = [new TeamSlotRequirementJob { Job = job, MinAttackPower = minAttack }] }
        });

    [Fact]
    public async Task GetCandidatesAsync_ExcludesCandidate_WhenAttackBelowRequirement()
    {
        SetupCandidatesPipeline(WarnCandidate()); // 攻擊 900
        SetRequirement("英雄", 1000, 0);          // 門檻 1000 → 900 不足

        Assert.Empty(await _service.GetCandidatesAsync(10));
    }

    [Fact]
    public async Task GetCandidatesAsync_IncludesCandidate_WhenAttackExactlyAtRequirement()
    {
        SetupCandidatesPipeline(WarnCandidate()); // 攻擊 900
        SetRequirement("英雄", 900, 0);           // 門檻剛好 900 → >= 命中（守 >= 對 > 邊界）

        Assert.Single(await _service.GetCandidatesAsync(10));
    }

    [Fact]
    public async Task GetCandidatesAsync_ExcludesCandidate_WhenJobMismatch()
    {
        SetupCandidatesPipeline(WarnCandidate()); // 英雄
        SetRequirement("法師", 0, 0);             // 需求職業不符 → 排除

        Assert.Empty(await _service.GetCandidatesAsync(10));
    }

    [Fact]
    public async Task GetCandidatesAsync_ExcludesCandidate_WhenClearCountBelowRequirement()
    {
        SetupCandidatesPipeline(WarnCandidate()); // BossClearCount 0
        SetRequirement("英雄", 0, 1);             // 需最低通關 1 → 0 不足

        Assert.Empty(await _service.GetCandidatesAsync(10));
    }

    [Fact]
    public async Task GetCandidatesAsync_ExcludesCandidate_WhenAlreadyActiveMember()
    {
        SetupCandidatesPipeline(WarnCandidate()); // DiscordId 777
        _memberRepositoryMock.Setup(r => r.GetActiveMemberDiscordIdsAsync(10)).ReturnsAsync(new HashSet<ulong> { 777 }); // 已在本隊 active → 去重

        Assert.Empty(await _service.GetCandidatesAsync(10));
    }

    // ── 即時團（§8 Phase 3）：候選來自 LfgIntent、跳過時段比對 ──
    [Fact]
    public async Task GetCandidatesAsync_UsesInstantPoolAndSkipsAvailability_WhenInstant()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new TeamSlot
        {
            Id = 10,
            BossId = 1,
            Kind = TeamSlotKind.Instant,
            SlotDateTime = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero)
        });
        _requirementRepositoryMock.Setup(r => r.GetByTeamSlotIdAsync(10)).ReturnsAsync(new[]
        {
            new TeamSlotRequirement { Count = 1, MinClearCount = 0, Jobs = [new TeamSlotRequirementJob { Job = "英雄", MinAttackPower = 0 }] }
        });
        _memberRepositoryMock.Setup(r => r.GetActiveMemberDiscordIdsAsync(10)).ReturnsAsync(new HashSet<ulong>());
        _memberRepositoryMock.Setup(r => r.GetConfirmedDiscordIdsAtAsync(It.IsAny<DateTimeOffset>())).ReturnsAsync(new HashSet<ulong>());
        // 候選無常設時段（Availabilities 空）→ 若誤走排程比對會被濾掉；即時應忽略時段仍納入
        _candidateQueryMock.Setup(q => q.GetInstantPoolAsync(1)).ReturnsAsync(new[]
        {
            new CandidatePoolItem { CharacterId = "c1", CharacterName = "C", DiscordId = 777, Job = "英雄", AttackPower = 900, Availabilities = [] }
        });

        var result = (await _service.GetCandidatesAsync(10)).ToList();

        Assert.Single(result);
        _candidateQueryMock.Verify(q => q.GetInstantPoolAsync(1), Times.Once);
        _candidateQueryMock.Verify(q => q.GetPoolAsync(It.IsAny<int>(), It.IsAny<DateTimeOffset>()), Times.Never);
        _candidateQueryMock.Verify(q => q.GetOverridesForDateAsync(It.IsAny<DateOnly>()), Times.Never);
    }

    [Fact]
    public async Task AcceptInviteAsync_ClearsLfgIntent_OnConfirm()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(InvitedMember()); // DiscordId=999
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(1)).ReturnsAsync(new Boss { Id = 1, Name = "王", RequireMembers = 6 });
        _memberRepositoryMock.Setup(r => r.CountConfirmedAsync(10)).ReturnsAsync(0);
        _memberRepositoryMock.Setup(r => r.UpdateStatusAsync(5, TeamSlotMemberStatus.Confirmed, "v1")).ReturnsAsync(true);

        await _service.AcceptInviteAsync(5, 999);

        _lfgIntentRepositoryMock.Verify(r => r.DeleteByDiscordIdAsync(999UL), Times.Once);
    }

    // ── 日期 override（§8 Phase 2b）：override 勝過常設 ──
    [Fact]
    public async Task GetCandidatesAsync_ExcludesCandidate_WhenDateOverrideMarksUnavailable()
    {
        SetupCandidatesPipeline(WarnCandidate()); // 常設週三整天可用 → 本會命中
        _candidateQueryMock.Setup(q => q.GetOverridesForDateAsync(It.IsAny<DateOnly>()))
            .ReturnsAsync(new[] { new AvailabilityOverrideItem { DiscordId = 777, StartTime = new TimeOnly(0, 0), EndTime = new TimeOnly(0, 0), IsAvailable = false } }); // 該日整天不行 → 蓋掉常設

        Assert.Empty(await _service.GetCandidatesAsync(10));
    }

    [Fact]
    public async Task GetCandidatesAsync_IncludesCandidate_WhenDateOverrideAddsAvailability()
    {
        // 常設落在週一（團在週三 20:00）→ 常設不命中；但該日 override 額外加開 19–22 → 命中
        var cand = WarnCandidate();
        cand.Availabilities = [new PlayerAvailability { Weekday = 1, StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(22, 0) }];
        SetupCandidatesPipeline(cand);
        _candidateQueryMock.Setup(q => q.GetOverridesForDateAsync(It.IsAny<DateOnly>()))
            .ReturnsAsync(new[] { new AvailabilityOverrideItem { DiscordId = 777, StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(22, 0), IsAvailable = true } });

        Assert.Single(await _service.GetCandidatesAsync(10));
    }

    [Fact]
    public async Task AcceptInviteAsync_ThrowsFull_WhenConfirmedEqualsCapacity()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(InvitedMember());
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(1)).ReturnsAsync(new Boss { Id = 1, Name = "王", RequireMembers = 1 });
        _memberRepositoryMock.Setup(r => r.CountConfirmedAsync(10)).ReturnsAsync(1); // 剛好等於容量 → 隊伍已滿（守 >= 對 > 邊界）

        await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(() => _service.AcceptInviteAsync(5, 999));
        _memberRepositoryMock.Verify(r => r.UpdateStatusAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProposeLeaderTransferAsync_ThrowsNotFound_WhenTargetInDifferentTeam()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new TeamSlotCharacter
        {
            Id = 5,
            TeamSlotId = 99, // 別隊成員
            DiscordId = 999,
            DiscordName = "X",
            Job = "英雄",
            Status = TeamSlotMemberStatus.Confirmed,
            Version = "v1"
        });

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(() => _service.ProposeLeaderTransferAsync(10, 5, 111));
        _teamSlotRepositoryMock.Verify(r => r.SetPendingLeaderAsync(It.IsAny<int>(), It.IsAny<ulong?>()), Times.Never);
    }

    [Fact]
    public async Task ProposeLeaderTransferAsync_ThrowsNotFound_WhenTargetNotConfirmed()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(InvitedMember()); // TeamSlotId=10 但 Status=Invited（非 Confirmed）

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(() => _service.ProposeLeaderTransferAsync(10, 5, 111));
        _teamSlotRepositoryMock.Verify(r => r.SetPendingLeaderAsync(It.IsAny<int>(), It.IsAny<ulong?>()), Times.Never);
    }

    // ── 授權/守衛分支（中批 A：leader-only / self-only / 狀態 / xmin 衝突；補 e2e happy path 測不到的錯誤路徑）──
    private static TeamSlotCharacter AppliedMember() => new()
    {
        Id = 5,
        TeamSlotId = 10,
        DiscordId = 999,
        DiscordName = "X",
        Job = "英雄",
        Status = TeamSlotMemberStatus.Applied,
        Version = "v1"
    };

    [Fact]
    public async Task InviteMemberAsync_ThrowsForbidden_WhenNotLeader()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<Application.Exceptions.ForbiddenException>(() => _service.InviteMemberAsync(10, "cX", 222));
        _memberRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<TeamSlotCharacter>()), Times.Never);
    }

    [Fact]
    public async Task InviteMemberAsync_ThrowsNotFound_WhenCharacterMissing()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });
        _bossRepositoryMock.Setup(b => b.GetByIdAsync(1)).ReturnsAsync(new Boss { Id = 1, Name = "王", RequireMembers = 6 });
        _memberRepositoryMock.Setup(r => r.CountConfirmedAsync(10)).ReturnsAsync(0);
        _characterQueryMock.Setup(q => q.GetByIdAsync("cX")).ReturnsAsync((Character?)null);

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(() => _service.InviteMemberAsync(10, "cX", 111));
        _memberRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<TeamSlotCharacter>()), Times.Never);
    }

    [Fact]
    public async Task AcceptInviteAsync_ThrowsForbidden_WhenNotSelf()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(InvitedMember()); // DiscordId=999

        await Assert.ThrowsAsync<Application.Exceptions.ForbiddenException>(() => _service.AcceptInviteAsync(5, 888));
        _memberRepositoryMock.Verify(r => r.UpdateStatusAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AcceptInviteAsync_ThrowsBusiness_WhenNotInvitedStatus()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(ConfirmedMember()); // 已 Confirmed，非 Invited

        await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(() => _service.AcceptInviteAsync(5, 999));
        _memberRepositoryMock.Verify(r => r.UpdateStatusAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeclineInviteAsync_ThrowsForbidden_WhenNotSelf()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(InvitedMember()); // DiscordId=999

        await Assert.ThrowsAsync<Application.Exceptions.ForbiddenException>(() => _service.DeclineInviteAsync(5, 888));
        _memberRepositoryMock.Verify(r => r.UpdateStatusAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeclineInviteAsync_ThrowsBusiness_WhenVersionConflict()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(InvitedMember());
        _memberRepositoryMock.Setup(r => r.UpdateStatusAsync(5, TeamSlotMemberStatus.Rejected, "v1")).ReturnsAsync(false); // xmin 對不上

        await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(() => _service.DeclineInviteAsync(5, 999));
    }

    [Fact]
    public async Task ApplyAsync_ThrowsNotFound_WhenCharacterNotOwned()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, SlotDateTime = DateTimeOffset.UtcNow });
        _characterQueryMock.Setup(q => q.GetByIdAsync("cX")).ReturnsAsync(new Character
        {
            Id = "cX",
            DiscordId = 777,
            Name = "C",
            Job = "英雄",
            AttackPower = 900 // 屬於別人（777≠999）
        });

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(() => _service.ApplyAsync(10, "cX", 999));
        _memberRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<TeamSlotCharacter>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_ThrowsNotFound_WhenMemberMissing()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync((TeamSlotCharacter?)null);

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(() => _service.ApproveAsync(5, 111));
    }

    [Fact]
    public async Task ApproveAsync_ThrowsBusiness_WhenNotAppliedStatus()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(ConfirmedMember()); // 非 Applied

        await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(() => _service.ApproveAsync(5, 111));
    }

    [Fact]
    public async Task ApproveAsync_ThrowsForbidden_WhenNotLeader()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(AppliedMember()); // TeamSlotId=10
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<Application.Exceptions.ForbiddenException>(() => _service.ApproveAsync(5, 222));
        _memberRepositoryMock.Verify(r => r.UpdateStatusAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RejectAsync_ThrowsForbidden_WhenNotLeader()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(AppliedMember());
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<Application.Exceptions.ForbiddenException>(() => _service.RejectAsync(5, 222));
        _memberRepositoryMock.Verify(r => r.UpdateStatusAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RejectAsync_ThrowsBusiness_WhenVersionConflict()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(AppliedMember());
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });
        _memberRepositoryMock.Setup(r => r.UpdateStatusAsync(5, TeamSlotMemberStatus.Rejected, "v1")).ReturnsAsync(false);

        await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(() => _service.RejectAsync(5, 111));
    }

    [Fact]
    public async Task GetApplicationsAsync_ThrowsForbidden_WhenNotLeader()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, LeaderDiscordId = 111, SlotDateTime = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<Application.Exceptions.ForbiddenException>(() => _service.GetApplicationsAsync(10, 222));
        _membershipQueryMock.Verify(q => q.GetApplicationsAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeclineInviteAsync_ThrowsBusiness_WhenNotInvitedStatus()
    {
        _memberRepositoryMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(ConfirmedMember()); // 自己、但已 Confirmed（非 Invited）

        await Assert.ThrowsAsync<Application.Exceptions.BusinessException>(() => _service.DeclineInviteAsync(5, 999));
        _memberRepositoryMock.Verify(r => r.UpdateStatusAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_ThrowsNotFound_WhenTeamMissing()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10)).ReturnsAsync((TeamSlot?)null);

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(() => _service.ApplyAsync(10, "cX", 999));
        _memberRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<TeamSlotCharacter>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_ThrowsNotFound_WhenCharacterMissing()
    {
        _teamSlotRepositoryMock.Setup(r => r.GetByIdAsync(10))
            .ReturnsAsync(new TeamSlot { Id = 10, BossId = 1, SlotDateTime = DateTimeOffset.UtcNow });
        _characterQueryMock.Setup(q => q.GetByIdAsync("cX")).ReturnsAsync((Character?)null);

        await Assert.ThrowsAsync<Application.Exceptions.NotFoundException>(() => _service.ApplyAsync(10, "cX", 999));
        _memberRepositoryMock.Verify(r => r.CreateAsync(It.IsAny<TeamSlotCharacter>()), Times.Never);
    }

    // ── 新鮮度心跳（plans/2026-09-01-availability-freshness-decay.md）：列舉每個生命週期動作都 bump 動作者 ──
    // bump 掛在方法首行 → 即使後續 setup 未備而拋例外，bump 呼叫仍已觸發（此測只驗「有接線、bump 對的人」；
    // 「失敗要 rollback 掉 bump」是 UoW/交易層行為，交整合測）。防「新增動作忘了 bump」（曾漏 CreateTeamAsync）。
    private async Task RunSwallow(Func<Task> act) { try { await act(); } catch { /* 只驗 bump 已觸發 */ } }

    [Fact] public async Task Bump_CreateTeam_Leader() { await RunSwallow(() => _service.CreateTeamAsync(ValidCommand())); _playerRepositoryMock.Verify(p => p.BumpLastAffirmedAsync(999UL), Times.Once); }
    [Fact] public async Task Bump_InviteMember_Leader() { await RunSwallow(() => _service.InviteMemberAsync(10, "c", 777UL)); _playerRepositoryMock.Verify(p => p.BumpLastAffirmedAsync(777UL), Times.Once); }
    [Fact] public async Task Bump_Apply_Applicant() { await RunSwallow(() => _service.ApplyAsync(10, "c", 111UL)); _playerRepositoryMock.Verify(p => p.BumpLastAffirmedAsync(111UL), Times.Once); }
    [Fact] public async Task Bump_AcceptInvite_Player() { await RunSwallow(() => _service.AcceptInviteAsync(5, 222UL)); _playerRepositoryMock.Verify(p => p.BumpLastAffirmedAsync(222UL), Times.Once); }
    [Fact] public async Task Bump_DeclineInvite_Player() { await RunSwallow(() => _service.DeclineInviteAsync(5, 333UL)); _playerRepositoryMock.Verify(p => p.BumpLastAffirmedAsync(333UL), Times.Once); }
    [Fact] public async Task Bump_Approve_Leader() { await RunSwallow(() => _service.ApproveAsync(5, 444UL)); _playerRepositoryMock.Verify(p => p.BumpLastAffirmedAsync(444UL), Times.Once); }
    [Fact] public async Task Bump_Reject_Leader() { await RunSwallow(() => _service.RejectAsync(5, 555UL)); _playerRepositoryMock.Verify(p => p.BumpLastAffirmedAsync(555UL), Times.Once); }
    [Fact] public async Task Bump_LeaveTeam_Member() { await RunSwallow(() => _service.LeaveTeamAsync(10, 666UL)); _playerRepositoryMock.Verify(p => p.BumpLastAffirmedAsync(666UL), Times.Once); }
    [Fact] public async Task Bump_RespondLeaderTransfer_Responder() { await RunSwallow(() => _service.RespondLeaderTransferAsync(10, 888UL, "accept")); _playerRepositoryMock.Verify(p => p.BumpLastAffirmedAsync(888UL), Times.Once); }
}
