using Application.DTOs;
using Application.Events;
using Application.Exceptions;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Helpers;
using Domain.Repositories;

namespace Infrastructure.Services;

public class TeamLeaderService : ITeamLeaderService
{
    private readonly IBossRepository _bossRepository;
    private readonly ITeamSlotRepository _teamSlotRepository;
    private readonly ITeamSlotRequirementRepository _requirementRepository;
    private readonly ITeamCandidateQuery _candidateQuery;
    private readonly ITeamSlotCharacterRepository _memberRepository;
    private readonly ICharacterQuery _characterQuery;
    private readonly IRegistrationLock _registrationLock;
    private readonly IOutbox _outbox;
    private readonly ITeamMembershipQuery _membershipQuery;
    private readonly ISystemConfigService _systemConfigService;
    private readonly ILfgIntentRepository _lfgIntentRepository;

    public TeamLeaderService(
        IBossRepository bossRepository,
        ITeamSlotRepository teamSlotRepository,
        ITeamSlotRequirementRepository requirementRepository,
        ITeamCandidateQuery candidateQuery,
        ITeamSlotCharacterRepository memberRepository,
        ICharacterQuery characterQuery,
        IRegistrationLock registrationLock,
        IOutbox outbox,
        ITeamMembershipQuery membershipQuery,
        ISystemConfigService systemConfigService,
        ILfgIntentRepository lfgIntentRepository)
    {
        _bossRepository = bossRepository;
        _teamSlotRepository = teamSlotRepository;
        _requirementRepository = requirementRepository;
        _candidateQuery = candidateQuery;
        _memberRepository = memberRepository;
        _characterQuery = characterQuery;
        _registrationLock = registrationLock;
        _outbox = outbox;
        _membershipQuery = membershipQuery;
        _systemConfigService = systemConfigService;
        _lfgIntentRepository = lfgIntentRepository;
    }

    // leader-led §11 通知：與狀態改動同交易 enqueue 一則 outbox（原子，崩了不遺失）→ bot handler 發 Discord DM。
    // target=0（如未認領隊無 leader）則略過。訊息在此組好（有王名/時段 context），handler 只負責送。
    private async Task NotifyAsync(int bossId, DateTimeOffset slot, ulong target, Func<string, string, string> buildMessage)
    {
        if (target == 0) return;
        var boss = await _bossRepository.GetByIdAsync(bossId);
        var bossName = boss?.Name ?? "王";
        var time = slot.ToOffset(TimeSpan.FromHours(8)).ToString("M/d HH:mm");
        await _outbox.EnqueueAsync(OutboxEventType.TeamNotification,
            new TeamNotificationEvent { TargetDiscordId = target, Message = buildMessage(bossName, time) });
    }

    public async Task<int> CreateTeamAsync(CreateTeamCommand command)
    {
        // Boss FK 前線檢查 → 404（見 plans/2026-08-06-validation-layering.md §2）
        if (await _bossRepository.GetByIdAsync(command.BossId) == null)
            throw new NotFoundException($"Boss {command.BossId} not found");

        // period-less（Phase 4d）：Period 承重牆已拆——排程團不再解析/綁 period，改驗時間本身合法：
        // 排程團(Scheduled)的 SlotDateTime 不得早於現在（過去時段無意義，也不會出現在時間窗看板）；
        // 即時團(Instant)時間＝現在、帶 TTL、候選來自 LfgIntent 看板。
        var isInstant = command.Kind == TeamSlotKind.Instant;
        if (!isInstant && command.SlotDateTime < DateTimeOffset.UtcNow)
            throw new BusinessException("排程開隊時間不得早於現在。");

        var teamSlotId = await _teamSlotRepository.CreateAsync(new TeamSlot
        {
            BossId = command.BossId,
            SlotDateTime = command.SlotDateTime,
            Source = TeamSlotSource.Leader,
            Kind = command.Kind,
            ExpiresAt = isInstant ? DateTimeOffset.UtcNow.AddHours(3) : null,
            LeaderDiscordId = command.LeaderDiscordId,
            Description = command.Description
        });

        foreach (var req in command.Requirements)
        {
            await _requirementRepository.CreateAsync(new TeamSlotRequirement
            {
                TeamSlotId = teamSlotId,
                Count = req.Count,
                MinClearCount = req.MinClearCount,
                Jobs = req.Jobs
                    .Select(j => new TeamSlotRequirementJob { Job = j.Job, MinAttackPower = j.MinAttackPower })
                    .ToList()
            });
        }

        return teamSlotId;
    }

    public async Task<IEnumerable<TeamCandidateDto>> GetCandidatesAsync(int teamSlotId)
    {
        var team = await _teamSlotRepository.GetByIdAsync(teamSlotId);
        if (team == null)
            throw new NotFoundException($"TeamSlot {teamSlotId} not found");

        var requirements = (await _requirementRepository.GetByTeamSlotIdAsync(teamSlotId)).ToList();
        // period-less：即時團(§8 Phase 3)候選來自 LfgIntent 看板（現在要打）；排程團候選 = 參戰中角色 × 常設可用時段。
        var isInstant = team.Kind == TeamSlotKind.Instant;
        var pool = isInstant
            ? await _candidateQuery.GetInstantPoolAsync(team.BossId)
            : await _candidateQuery.GetPoolAsync(team.BossId);
        // 狀態感知去重：排除「其玩家已在本隊 active（Confirmed/Invited/Applied）」者——避免重列已入隊/待處理、再邀撞 409。
        // 以 DiscordId 為準（active-membership 一人一隊一個）；保留 Rejected/Left → 位子重開時可重邀。
        var activeIds = await _memberRepository.GetActiveMemberDiscordIdsAsync(teamSlotId);
        // 不可分身：排除「已在該開團時刻別隊 Confirmed」者（對齊 uq_tsc_confirmed_overlap；否則邀了也接受不了）。
        var bookedIds = await _memberRepository.GetConfirmedDiscordIdsAtAsync(team.SlotDateTime);

        // 團時間 → weekday/time/date（TPE），比照 TeamSlotAutoAssignService 慣例
        var twTime = team.SlotDateTime.ToOffset(TimeSpan.FromHours(8));
        int teamWeekday = SlotDateCalculator.ToIsoWeekday(twTime.DayOfWeek);
        var teamTime = TimeOnly.FromDateTime(twTime.DateTime);
        var teamDate = DateOnly.FromDateTime(twTime.DateTime);
        // 日期 override（§8 Phase 2b）：僅排程團需要（即時團不看時段）。
        var overridesByDiscord = isInstant
            ? new Dictionary<ulong, List<AvailabilityOverrideItem>>()
            : (await _candidateQuery.GetOverridesForDateAsync(teamDate))
                .GroupBy(o => o.DiscordId)
                .ToDictionary(g => g.Key, g => g.ToList());

        var matched = pool
            .Where(item =>
                // 排除已在本隊 active 的玩家（去重）
                !activeIds.Contains(item.DiscordId)
                // 排除已在該時刻別隊 Confirmed（不可分身）
                && !bookedIds.Contains(item.DiscordId)
                // 排程團看時段重疊（常設 + override）；即時團跳過（他們現在就要打）
                && (isInstant || IsAvailableAt(item, teamWeekday, teamTime, overridesByDiscord))
                // 且符合至少一需求列：某可接受職業==角色職業 且 攻擊≥該職下限 且 本王通關≥該列門檻。
                // 無需求列 → 無候選（隊長須先定義條件才看得到候選）。
                && requirements.Any(r =>
                    item.BossClearCount >= r.MinClearCount &&
                    r.Jobs.Any(j => j.Job == item.Job && item.AttackPower >= j.MinAttackPower)))
            .ToList();

        // 退團率信號（Feature 1b，admin 開才算才回）：窗內退團率達門檻者標警示。
        IReadOnlyCollection<ulong> warnIds = [];
        var config = await _systemConfigService.GetAsync();
        if (config.LeaveRateWarnEnabled && matched.Count > 0)
        {
            var windowStart = DateTimeOffset.UtcNow.AddMonths(-config.LeaveRateWindowMonths);
            warnIds = await _candidateQuery.GetHighLeaveRateDiscordIdsAsync(
                matched.Select(m => m.DiscordId), windowStart, config.LeaveRateMinSample, config.LeaveRateThreshold);
        }

        return matched
            .Select(item => new TeamCandidateDto
            {
                CharacterId = item.CharacterId,
                CharacterName = item.CharacterName,
                DiscordName = item.DiscordName,
                Job = item.Job,
                AttackPower = item.AttackPower,
                MapleBlessingLevel = item.MapleBlessingLevel,
                BossClearCount = item.BossClearCount,
                LeaveRateWarn = warnIds.Contains(item.DiscordId)
            })
            .ToList();
    }

    // override-aware 可用判定（§8 Phase 2b）：override 勝過常設——該時段有「不行」→ false；有「加開」→ true；否則看常設 pattern。
    private static bool IsAvailableAt(CandidatePoolItem item, int teamWeekday, TimeOnly teamTime,
        IReadOnlyDictionary<ulong, List<AvailabilityOverrideItem>> overridesByDiscord)
    {
        if (overridesByDiscord.TryGetValue(item.DiscordId, out var ovs))
        {
            if (ovs.Any(o => !o.IsAvailable && SlotDateCalculator.IsTimeInWindow(teamTime, o.StartTime, o.EndTime)))
                return false;
            if (ovs.Any(o => o.IsAvailable && SlotDateCalculator.IsTimeInWindow(teamTime, o.StartTime, o.EndTime)))
                return true;
        }
        return item.Availabilities.Any(a => SlotDateCalculator.IsTimeInAvailability(teamWeekday, teamTime, a));
    }

    public async Task InviteMemberAsync(int teamSlotId, string characterId, ulong leaderDiscordId)
    {
        var team = await _teamSlotRepository.GetByIdAsync(teamSlotId);
        if (team == null)
            throw new NotFoundException($"TeamSlot {teamSlotId} not found");
        if (team.LeaderDiscordId != leaderDiscordId)
            throw new ForbiddenException("只有隊長能邀請成員。");

        // 不允許超額邀請：隊伍已滿（Confirmed 達容量）→ 擋下新邀請（前線 400）。
        // 只擋「滿隊再邀」；未滿時仍可邀超過剩餘位數的候選（Pull 常態：多邀幾人搶位，滿了由 Tier 3 自動撤銷其餘）。
        // 容量整合性由 confirm 端 advisory lock 重數把關；此處為 UX 前線守衛（見 plans/2026-08-06-validation-layering.md）。
        var boss = await _bossRepository.GetByIdAsync(team.BossId);
        var capacity = boss?.RequireMembers ?? 6;
        if (await _memberRepository.CountConfirmedAsync(teamSlotId) >= capacity)
            throw new BusinessException("隊伍已滿，無法再邀請。");

        var character = await _characterQuery.GetByIdAsync(characterId);
        if (character == null)
            throw new NotFoundException($"Character {characterId} not found");

        // 快照角色屬性（base 攻擊信任模型；§3 承諾快照——邀請時填、accept 定格）。
        // DiscordName 承諾前不揭露（§9.11）→ 邀請時留空，之後可於顯示層 join。
        // 重複邀請（同隊同人已有 Applied/Invited）由 DB unique uq_tsc_active_membership → 23505 → 409。
        await _memberRepository.CreateAsync(new TeamSlotCharacter
        {
            TeamSlotId = teamSlotId,
            DiscordId = character.DiscordId,
            DiscordName = "",
            CharacterId = character.Id,
            CharacterName = character.Name,
            Job = character.Job,
            AttackPower = character.AttackPower,
            Status = TeamSlotMemberStatus.Invited,
            SlotDateTime = team.SlotDateTime,
            IsManual = true
        });

        // 通知被邀玩家
        await NotifyAsync(team.BossId, team.SlotDateTime, character.DiscordId,
            (boss, time) => $"隊長邀請你加入「{boss}」{time} 的隊伍，請至站內接受或拒絕。");
    }

    public async Task AcceptInviteAsync(int memberId, ulong currentDiscordId)
    {
        var member = await _memberRepository.GetByIdAsync(memberId);
        if (member == null)
            throw new NotFoundException($"Invitation {memberId} not found");
        if (member.DiscordId != currentDiscordId)
            throw new ForbiddenException("只能接受自己的邀請。");
        if (member.Status != TeamSlotMemberStatus.Invited)
            throw new BusinessException("此邀請目前無法接受（狀態已變）。");

        await ConfirmMemberAsync(member);

        // 通知隊長：邀請被接受
        var team = await _teamSlotRepository.GetByIdAsync(member.TeamSlotId);
        if (team != null)
            await NotifyAsync(team.BossId, team.SlotDateTime, team.LeaderDiscordId ?? 0,
                (boss, time) => $"你「{boss}」{time} 隊伍的一則邀請已被接受、成員入隊。");
    }

    /// <summary>
    /// 把一筆 Invited/Applied 成員定案成 Confirmed（accept〔玩家〕/ approve〔隊長〕共用）：
    /// 悲觀鎖（per-team）→ 重讀 Confirmed 數 → 檢查容量 → xmin 改狀態。序列化同隊多方 confirm，防超編；
    /// 跨隊時段重疊由 uq_tsc_confirmed_overlap → 23505 → ExceptionHandlerMiddleware 轉 409。呼叫端已各自做完授權。
    /// </summary>
    private async Task ConfirmMemberAsync(TeamSlotCharacter member)
    {
        try
        {
            await _registrationLock.AcquireTeamSlotEditLockAsync(member.TeamSlotId);
        }
        catch (AdvisoryLockTimeoutException)
        {
            throw new BusinessException("隊伍忙碌中，請稍後重試。");
        }

        var team = await _teamSlotRepository.GetByIdAsync(member.TeamSlotId);
        if (team == null)
            throw new NotFoundException($"TeamSlot {member.TeamSlotId} not found");

        var boss = await _bossRepository.GetByIdAsync(team.BossId);
        var capacity = boss?.RequireMembers ?? 6;
        if (await _memberRepository.CountConfirmedAsync(member.TeamSlotId) >= capacity)
            throw new BusinessException("隊伍已滿。");

        var ok = await _memberRepository.UpdateStatusAsync(member.Id!.Value, TeamSlotMemberStatus.Confirmed, member.Version!);
        if (!ok)
            throw new BusinessException("狀態已被更新，請重新整理。");

        // period-less §8 Phase 3：入隊後清掉該玩家的找隊意圖（已找到隊、不再掛看板）。scheduled accept 無意圖 → no-op。
        await _lfgIntentRepository.DeleteByDiscordIdAsync(member.DiscordId);

        // mutation-ux Tier 3：本次定案若使隊伍額滿 → 自動撤銷其餘待接受邀請（否則玩家端只剩無法按的死按鈕、隊長邀請數也虛掛）。
        // 仍在 per-team advisory lock 內，與其他 confirm 序列化，不會與「同時另一人接受」競態。
        if (await _memberRepository.CountConfirmedAsync(member.TeamSlotId) >= capacity)
        {
            var revokedDiscordIds = await _memberRepository.RevokePendingInvitesAsync(member.TeamSlotId);
            foreach (var discordId in revokedDiscordIds)
                await NotifyAsync(team.BossId, team.SlotDateTime, discordId,
                    (boss, time) => $"「{boss}」{time} 的隊伍已額滿，你的邀請自動失效。");
        }
    }

    public async Task DeclineInviteAsync(int memberId, ulong currentDiscordId)
    {
        var member = await _memberRepository.GetByIdAsync(memberId);
        if (member == null)
            throw new NotFoundException($"Invitation {memberId} not found");
        if (member.DiscordId != currentDiscordId)
            throw new ForbiddenException("只能拒絕自己的邀請。");
        if (member.Status != TeamSlotMemberStatus.Invited)
            throw new BusinessException("此邀請目前無法拒絕（狀態已變）。");

        var ok = await _memberRepository.UpdateStatusAsync(memberId, TeamSlotMemberStatus.Rejected, member.Version!);
        if (!ok)
            throw new BusinessException("邀請狀態已被更新，請重新整理。");

        // 通知隊長：邀請被拒絕
        var team = await _teamSlotRepository.GetByIdAsync(member.TeamSlotId);
        if (team != null)
            await NotifyAsync(team.BossId, team.SlotDateTime, team.LeaderDiscordId ?? 0,
                (boss, time) => $"你「{boss}」{time} 隊伍的一則邀請已被拒絕。");
    }

    public async Task ApplyAsync(int teamSlotId, string characterId, ulong applicantDiscordId)
    {
        var team = await _teamSlotRepository.GetByIdAsync(teamSlotId);
        if (team == null)
            throw new NotFoundException($"TeamSlot {teamSlotId} not found");

        // 申請須用本人角色（存在 + 擁有權）→ 404；快照角色屬性（同邀請）。
        var character = await _characterQuery.GetByIdAsync(characterId);
        if (character == null || character.DiscordId != applicantDiscordId)
            throw new NotFoundException($"Character {characterId} not found");

        // 重複申請（同隊同人已有 Applied/Invited）由 DB unique uq_tsc_active_membership → 23505 → 409。
        await _memberRepository.CreateAsync(new TeamSlotCharacter
        {
            TeamSlotId = teamSlotId,
            DiscordId = applicantDiscordId,
            DiscordName = "",
            CharacterId = character.Id,
            CharacterName = character.Name,
            Job = character.Job,
            AttackPower = character.AttackPower,
            Status = TeamSlotMemberStatus.Applied,
            SlotDateTime = team.SlotDateTime,
            IsManual = true
        });

        // 通知隊長有新申請
        await NotifyAsync(team.BossId, team.SlotDateTime, team.LeaderDiscordId ?? 0,
            (boss, time) => $"有玩家申請加入你「{boss}」{time} 的隊伍，請至站內審核。");
    }

    public async Task ApproveAsync(int memberId, ulong leaderDiscordId)
    {
        var member = await _memberRepository.GetByIdAsync(memberId);
        if (member == null)
            throw new NotFoundException($"Application {memberId} not found");
        if (member.Status != TeamSlotMemberStatus.Applied)
            throw new BusinessException("此申請目前無法核准（狀態已變）。");

        var team = await EnsureLeaderOwnsTeamAsync(member.TeamSlotId, leaderDiscordId, "只有隊長能核准申請。");
        await ConfirmMemberAsync(member);

        // 通知申請玩家：通過入隊
        await NotifyAsync(team.BossId, team.SlotDateTime, member.DiscordId,
            (boss, time) => $"你申請的「{boss}」{time} 隊伍已通過、成功入隊。");
    }

    public async Task RejectAsync(int memberId, ulong leaderDiscordId)
    {
        var member = await _memberRepository.GetByIdAsync(memberId);
        if (member == null)
            throw new NotFoundException($"Application {memberId} not found");
        if (member.Status != TeamSlotMemberStatus.Applied)
            throw new BusinessException("此申請目前無法拒絕（狀態已變）。");

        var team = await EnsureLeaderOwnsTeamAsync(member.TeamSlotId, leaderDiscordId, "只有隊長能拒絕申請。");

        var ok = await _memberRepository.UpdateStatusAsync(memberId, TeamSlotMemberStatus.Rejected, member.Version!);
        if (!ok)
            throw new BusinessException("狀態已被更新，請重新整理。");

        // 通知申請玩家：未通過
        await NotifyAsync(team.BossId, team.SlotDateTime, member.DiscordId,
            (boss, time) => $"你申請的「{boss}」{time} 隊伍未通過。");
    }

    private async Task<TeamSlot> EnsureLeaderOwnsTeamAsync(int teamSlotId, ulong leaderDiscordId, string forbiddenMessage)
    {
        var team = await _teamSlotRepository.GetByIdAsync(teamSlotId);
        if (team == null)
            throw new NotFoundException($"TeamSlot {teamSlotId} not found");
        if (team.LeaderDiscordId != leaderDiscordId)
            throw new ForbiddenException(forbiddenMessage);
        return team;
    }

    public Task<IEnumerable<MembershipDto>> GetMyInvitationsAsync(ulong discordId)
        => _membershipQuery.GetByDiscordIdAndStatusAsync(discordId, TeamSlotMemberStatus.Invited);

    public Task<IEnumerable<MembershipDto>> GetMyTeamsAsync(ulong discordId)
        => _membershipQuery.GetByDiscordIdAndStatusAsync(discordId, TeamSlotMemberStatus.Confirmed);

    public async Task<IEnumerable<MembershipDto>> GetApplicationsAsync(int teamSlotId, ulong leaderDiscordId)
    {
        await EnsureLeaderOwnsTeamAsync(teamSlotId, leaderDiscordId, "只有隊長能查看申請。");
        return await _membershipQuery.GetApplicationsAsync(teamSlotId);
    }

    public async Task<IEnumerable<OpenTeamDto>> GetOpenTeamsAsync()
        // period-less §8 Phase 4a：時間窗取代 period（未來排程 + 未過期即時），不再吃 active period。
        => await _membershipQuery.GetOpenTeamsAsync();

    public async Task<IEnumerable<LedTeamDto>> GetLedTeamsAsync(ulong leaderDiscordId)
        // period-less §8 Phase 4a：時間窗取代 period。
        => await _membershipQuery.GetLedTeamsAsync(leaderDiscordId);

    public async Task LeaveTeamAsync(int teamSlotId, ulong currentDiscordId)
    {
        // 只能退自己在該隊的 Confirmed 成員資格（一人一隊至多一個 Confirmed）。查無 → 不在此隊/已退。
        var member = await _memberRepository.GetConfirmedMemberAsync(teamSlotId, currentDiscordId);
        if (member == null)
            throw new NotFoundException("你不在此隊、或已退出。");

        // Confirmed→Left（xmin）。退隊只減 Confirmed → 位子自動重開（容量/開放隊/我的隊皆按 Confirmed 計）。
        var ok = await _memberRepository.LeaveAsync(member.Id!.Value, member.Version!);
        if (!ok)
            throw new BusinessException("狀態已被更新，請重新整理。");

        // 通知隊長：有人退隊、位子重開
        var team = await _teamSlotRepository.GetByIdAsync(teamSlotId);
        if (team != null)
            await NotifyAsync(team.BossId, team.SlotDateTime, team.LeaderDiscordId ?? 0,
                (boss, time) => $"有成員退出你「{boss}」{time} 的隊伍，位子已重開。");
    }

    public async Task ProposeLeaderTransferAsync(int teamSlotId, int memberId, ulong currentDiscordId)
    {
        var team = await EnsureLeaderOwnsTeamAsync(teamSlotId, currentDiscordId, "只有隊長能轉讓。");

        // 目標須為本隊的 Confirmed 成員（MVP）；不能轉給自己。
        var member = await _memberRepository.GetByIdAsync(memberId);
        if (member == null || member.TeamSlotId != teamSlotId || member.Status != TeamSlotMemberStatus.Confirmed)
            throw new NotFoundException("轉讓目標必須是本隊的已入隊成員。");
        if (member.DiscordId == currentDiscordId)
            throw new BusinessException("不能把隊長轉給自己。");

        await _teamSlotRepository.SetPendingLeaderAsync(teamSlotId, member.DiscordId);
        await NotifyAsync(team.BossId, team.SlotDateTime, member.DiscordId,
            (boss, time) => $"隊長想把「{boss}」{time} 的隊長轉給你，請至站內接受或拒絕。");
    }

    public async Task RespondLeaderTransferAsync(int teamSlotId, ulong currentDiscordId, string action)
    {
        var team = await _teamSlotRepository.GetByIdAsync(teamSlotId);
        if (team == null)
            throw new NotFoundException($"TeamSlot {teamSlotId} not found");
        if (team.PendingLeaderDiscordId != currentDiscordId)
            throw new ForbiddenException("沒有指定給你的待處理轉讓。");

        var oldLeader = team.LeaderDiscordId ?? 0;
        switch (action)
        {
            case "accept":
                await _teamSlotRepository.CompleteLeaderTransferAsync(teamSlotId, currentDiscordId);
                await NotifyAsync(team.BossId, team.SlotDateTime, oldLeader,
                    (boss, time) => $"你「{boss}」{time} 的隊長轉讓已被接受、對方成為新隊長。");
                break;
            case "decline":
                await _teamSlotRepository.SetPendingLeaderAsync(teamSlotId, null);
                await NotifyAsync(team.BossId, team.SlotDateTime, oldLeader,
                    (boss, time) => $"你「{boss}」{time} 的隊長轉讓被拒絕。");
                break;
            default:
                throw new BusinessException("無效的動作。");
        }
    }

    public Task<IEnumerable<LeaderTransferDto>> GetMyLeaderTransfersAsync(ulong discordId)
        => _membershipQuery.GetPendingLeaderTransfersAsync(discordId);

    public async Task<IEnumerable<RosterMemberDto>> GetTeamRosterAsync(int teamSlotId, ulong leaderDiscordId)
    {
        await EnsureLeaderOwnsTeamAsync(teamSlotId, leaderDiscordId, "只有隊長能查看名冊。");
        return await _membershipQuery.GetConfirmedRosterAsync(teamSlotId);
    }
}
