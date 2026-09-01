using Application.DTOs;
using Application.Events;
using Application.Exceptions;
using Application.Interface;
using Application.Options;
using Application.Queries;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Helpers;
using Domain.Repositories;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class TeamLeaderService : ITeamLeaderService
{
    private readonly IBossRepository _bossRepository;
    private readonly ITeamSlotRepository _teamSlotRepository;
    private readonly ITeamSlotRequirementRepository _requirementRepository;
    private readonly ITeamCandidateQuery _candidateQuery;
    private readonly ITeamSlotCharacterRepository _memberRepository;
    private readonly ICharacterQuery _characterQuery;
    private readonly ITeamSlotEditLock _teamSlotEditLock;
    private readonly IOutbox _outbox;
    private readonly ITeamMembershipQuery _membershipQuery;
    private readonly ISystemConfigService _systemConfigService;
    private readonly ILfgIntentRepository _lfgIntentRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly string _appUrl;

    public TeamLeaderService(
        IBossRepository bossRepository,
        ITeamSlotRepository teamSlotRepository,
        ITeamSlotRequirementRepository requirementRepository,
        ITeamCandidateQuery candidateQuery,
        ITeamSlotCharacterRepository memberRepository,
        ICharacterQuery characterQuery,
        ITeamSlotEditLock teamSlotEditLock,
        IOutbox outbox,
        ITeamMembershipQuery membershipQuery,
        ISystemConfigService systemConfigService,
        ILfgIntentRepository lfgIntentRepository,
        IPlayerRepository playerRepository,
        IOptions<AppOptions> appOptions)
    {
        _bossRepository = bossRepository;
        _teamSlotRepository = teamSlotRepository;
        _requirementRepository = requirementRepository;
        _candidateQuery = candidateQuery;
        _memberRepository = memberRepository;
        _characterQuery = characterQuery;
        _teamSlotEditLock = teamSlotEditLock;
        _outbox = outbox;
        _membershipQuery = membershipQuery;
        _systemConfigService = systemConfigService;
        _lfgIntentRepository = lfgIntentRepository;
        _playerRepository = playerRepository;
        _appUrl = appOptions.Value.AppUrl;
    }

    // 心跳（常設時段新鮮度衰退，plans/2026-09-01-availability-freshness-decay.md）：組隊實質動作後 bump
    // 動作者的 LastAffirmedAt（節流在 repository、僅隔日才真寫）。放共用 Application 層 → web + bot 兩路徑
    // 皆涵蓋（bot 的接受/婉拒直呼本 service、繞過 HTTP）。與動作同交易：動作失敗 rollback 會連 bump 一起退 → 只有成功動作留痕。
    private Task BumpActivityAsync(ulong actorDiscordId) => _playerRepository.BumpLastAffirmedAsync(actorDiscordId);

    // 新鮮度下限 = now − admin 設定天數（SystemConfig.AvailabilityFreshnessDays）。供排程候選池濾掉 stale opt-in。
    private async Task<DateTimeOffset> GetFreshnessSinceAsync() =>
        DateTimeOffset.UtcNow.AddDays(-(await _systemConfigService.GetAsync()).AvailabilityFreshnessDays);

    // leader-led §11 通知：與狀態改動同交易 enqueue 一則 outbox（原子，崩了不遺失）→ bot handler 發 Discord DM。
    // target=0（如未認領隊無 leader）則略過。訊息在此組好（有王名/時段 context），handler 只負責送。
    private async Task NotifyAsync(int bossId, DateTimeOffset slot, ulong target, string path,
        Func<string, string, string> buildMessage,
        TeamNotificationAction action = TeamNotificationAction.None, int? actionId = null,
        TeamEmbedData? embed = null)
    {
        if (target == 0) return;
        var boss = await _bossRepository.GetByIdAsync(bossId);
        var bossName = boss?.Name ?? "王";
        var time = slot.ToOffset(TimeSpan.FromHours(8)).ToString("M/d HH:mm");
        var message = buildMessage(bossName, time);
        // InviteResponse：DM 用「接受/拒絕」按鈕直接在 Discord 內操作 → 不附連結（避免雜訊，discord-inline-actions）。
        // 其餘通知末尾附「該通知對應站內頁」深連結（點了直接到那頁）；未設 AppUrl 時不附。
        if (action == TeamNotificationAction.None && !string.IsNullOrWhiteSpace(_appUrl))
            message += $"\n{_appUrl}{path}";
        await _outbox.EnqueueAsync(OutboxEventType.TeamNotification,
            new TeamNotificationEvent { TargetDiscordId = target, Message = message, Action = action, ActionId = actionId, Embed = embed });
    }

    // dm-revoke-cleanup：enqueue 一則「編輯被邀者 DM 成已失效 + 移按鈕」事件。走 outbox（非直接呼叫 Discord）——
    // 因 ConfirmMember 可能跑在無 DiscordClient 的 WebApi 行程，編輯動作一律交 bot 端 handler 執行。
    private Task EnqueueInviteRevokedCleanupAsync(ulong target, ulong messageId, string message) =>
        _outbox.EnqueueAsync(OutboxEventType.TeamNotification,
            new TeamNotificationEvent
            {
                TargetDiscordId = target,
                Message = message,
                Action = TeamNotificationAction.InviteRevokedCleanup,
                EditMessageId = messageId
            });

    // 組 embed 快照（王/時段/容量 + 目前 Confirmed 成員能力）；「主角角色」(被邀/申請者) 由呼叫端另填，轉讓無主角。
    private async Task<TeamEmbedData> BuildEmbedSnapshotAsync(TeamSlot team, int teamSlotId)
    {
        var boss = await _bossRepository.GetByIdAsync(team.BossId);
        var confirmed = await _membershipQuery.GetConfirmedMembersAsync(teamSlotId);
        return new TeamEmbedData
        {
            BossName = boss?.Name ?? "王",
            TimeText = team.SlotDateTime.ToOffset(TimeSpan.FromHours(8)).ToString("M/d HH:mm"),
            Capacity = boss?.RequireMembers ?? 6,
            Roster = confirmed.Select(m => new RosterEntry
            {
                Job = m.Job ?? "",
                AttackPower = m.AttackPower,
                Level = m.Level,
                MapleBlessingLevel = m.MapleBlessingLevel
            }).ToList()
        };
    }

    public async Task<int> CreateTeamAsync(CreateTeamCommand command)
    {
        await BumpActivityAsync(command.LeaderDiscordId);   // 心跳：開團＝最強活躍訊號（含不帶角色純揪人）
        // Boss FK 前線檢查 → 404（見 plans/2026-08-06-validation-layering.md §2）
        var boss = await _bossRepository.GetByIdAsync(command.BossId);
        if (boss == null)
            throw new NotFoundException($"Boss {command.BossId} not found");

        // composition-quota：需求人數總和不可超過隊伍容量（否則未指定名額為負、需求與容量矛盾）。
        var totalRequired = command.Requirements.Sum(r => r.Count);
        if (totalRequired > boss.RequireMembers)
            throw new BusinessException($"需求人數總和（{totalRequired}）不可超過隊伍容量（{boss.RequireMembers}）。");

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
                MinLevel = req.MinLevel,
                Jobs = req.Jobs
                    .Select(j => new TeamSlotRequirementJob { Job = j.Job, MinAttackPower = j.MinAttackPower })
                    .ToList()
            });
        }

        // 隊長帶自己的角色下去打 → 佔 1 位、自動 Confirmed（null = 只揪人、自己不打）。
        if (!string.IsNullOrEmpty(command.LeaderCharacterId))
        {
            var character = await _characterQuery.GetByIdAsync(command.LeaderCharacterId);
            if (character == null || character.DiscordId != command.LeaderDiscordId)
                throw new NotFoundException($"Character {command.LeaderCharacterId} not found");

            // 直接 Confirmed（隊長自己不用邀請/審核）。跨隊同時段重疊由 uq_tsc_confirmed_overlap → 23505 → 409 擋。
            await _memberRepository.CreateAsync(new TeamSlotCharacter
            {
                TeamSlotId = teamSlotId,
                DiscordId = command.LeaderDiscordId,
                DiscordName = "",
                CharacterId = character.Id,
                CharacterName = character.Name,
                Job = character.Job,
                AttackPower = character.AttackPower,
                Level = character.Level,
                Status = TeamSlotMemberStatus.Confirmed,
                SlotDateTime = command.SlotDateTime,
                IsManual = true
            });
        }

        return teamSlotId;
    }

    public async Task DeleteTeamAsync(int teamSlotId, ulong leaderDiscordId)
    {
        var team = await EnsureLeaderOwnsTeamAsync(teamSlotId, leaderDiscordId, "只有隊長能刪除隊伍。");

        // 先撈 active 成員（Confirmed/Invited/Applied）——刪隊後成員列就沒了，撈不到。
        var affected = await _memberRepository.GetActiveMemberDiscordIdsAsync(teamSlotId);

        // 連帶清成員列 + 隊伍本身（TeamSlotRepository.DeleteAsync 先刪 TeamSlotCharacter 再刪 TeamSlot）。
        await _teamSlotRepository.DeleteAsync(teamSlotId);

        // 通知每位受影響成員：隊伍已解散（他們的 Confirmed/邀請/申請都一併失效）。
        // 排除隊長本人——是他按的解散，不用通知自己。
        foreach (var discordId in affected.Where(id => id != leaderDiscordId))
            await NotifyAsync(team.BossId, team.SlotDateTime, discordId, "/me/teams",
                (boss, time) => $"隊長已解散「{boss}」{time} 的隊伍。");
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
            : await _candidateQuery.GetPoolAsync(team.BossId, await GetFreshnessSinceAsync());
        // 狀態感知去重：排除「其玩家已在本隊 active（Confirmed/Invited/Applied）」者——避免重列已入隊/待處理、再邀撞 409。
        // 以 DiscordId 為準（active-membership 一人一隊一個）；保留 Rejected/Left → 位子重開時可重邀。
        var activeIds = await _memberRepository.GetActiveMemberDiscordIdsAsync(teamSlotId);
        // 不可分身：排除「已在該開團時刻別隊 Confirmed」者（對齊 uq_tsc_confirmed_overlap；否則邀了也接受不了）。
        var bookedIds = await _memberRepository.GetConfirmedDiscordIdsAtAsync(team.SlotDateTime);

        // 團時間 → weekday/time/date（TPE）換算。
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
                // 且符合至少一需求列（見 IsCandidateEligible）。無需求列 → 無候選（隊長須先定義條件才看得到候選）。
                && IsCandidateEligible(item, requirements))
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
            // 偏好王軟訊號排序：偏好本王 → 沒設偏好(中性) → 設了但不含本王(殿後)。
            // OrderBy 穩定排序 → 同層維持原 DISTINCT 次序；皆未被排除（守 boss-agnostic）。
            .OrderByDescending(item => item.PrefersThisBoss)
            .ThenBy(item => item.HasAnyPreference)
            .Select(item => new TeamCandidateDto
            {
                CharacterId = item.CharacterId,
                CharacterName = item.CharacterName,
                DiscordName = item.DiscordName,
                Job = item.Job,
                AttackPower = item.AttackPower,
                Level = item.Level,
                MapleBlessingLevel = item.MapleBlessingLevel,
                BossClearCount = item.BossClearCount,
                LeaveRateWarn = warnIds.Contains(item.DiscordId),
                PrefersThisBoss = item.PrefersThisBoss
            })
            .ToList();
    }

    public async Task<RecruitmentHeatmapDto> GetRecruitmentHeatmapAsync(RecruitmentHeatmapCommand command)
    {
        if (await _bossRepository.GetByIdAsync(command.BossId) == null)
            throw new NotFoundException($"Boss {command.BossId} not found");

        // 草稿需求（尚未建隊）→ 轉 Domain 型別供過濾/匹配共用。
        var requirements = command.Requirements.Select(r => new TeamSlotRequirement
        {
            Count = r.Count,
            MinClearCount = r.MinClearCount,
            MinLevel = r.MinLevel,
            Jobs = r.Jobs.Select(j => new TeamSlotRequirementJob { Job = j.Job, MinAttackPower = j.MinAttackPower }).ToList()
        }).ToList();
        var totalRequired = requirements.Sum(r => r.Count);
        var result = new RecruitmentHeatmapDto { TotalRequired = totalRequired };
        if (totalRequired == 0)
            return result;   // 無需求 → 空熱力圖

        // 池撈一次 + 依門檻（職業/攻擊/等級/通關）過濾一次（與時間無關）。即時池不看時段、對熱力圖無意義 → 用排程池。
        var eligible = (await _candidateQuery.GetPoolAsync(command.BossId, await GetFreshnessSinceAsync()))
            .Where(p => IsCandidateEligible(p, requirements))
            .ToList();
        if (eligible.Count == 0)
            return result;   // 沒人合格 → 回空 cells（前端全灰）

        // 顯示哪些整點：候選常設時段實際覆蓋到的整點（weekday-agnostic）。含跨午夜 wrap（22:00–02:00 → 也涵蓋 00/01），
        // 這些凌晨格會在「隔天」欄依 IsTimeInAvailability 的 wrap 分支正確亮起。非連續也 OK（前端由 cells 推列）。
        var slots = eligible.SelectMany(e => e.Availabilities).ToList();
        var hours = Enumerable.Range(0, 24).Where(h => slots.Any(a => HourInWindow(h, a))).ToList();
        if (hours.Count == 0)
            hours = Enumerable.Range(18, 6).ToList();   // fallback 18–23

        var days = Math.Clamp(command.Days, 1, 30);
        var nowTpe = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
        var startDate = DateOnly.FromDateTime(nowTpe.DateTime);

        // 一次撈範圍內全部 Confirmed 訂位 → 依精確 SlotDateTime(UTC) 分格扣「不可分身」。
        var rangeStart = new DateTimeOffset(startDate.ToDateTime(new TimeOnly(hours.Min(), 0)), TimeSpan.FromHours(8));
        var rangeEnd = new DateTimeOffset(startDate.AddDays(days - 1).ToDateTime(new TimeOnly(hours.Max(), 0)), TimeSpan.FromHours(8));
        var bookings = (await _memberRepository.GetConfirmedBookingsInRangeAsync(rangeStart, rangeEnd))
            .GroupBy(b => b.SlotDateTime.ToUniversalTime())
            .ToDictionary(g => g.Key, g => g.Select(x => x.DiscordId).ToHashSet());

        for (var d = 0; d < days; d++)
        {
            var date = startDate.AddDays(d);
            var weekday = SlotDateCalculator.ToIsoWeekday(date.ToDateTime(TimeOnly.MinValue).DayOfWeek);
            var overridesByDiscord = (await _candidateQuery.GetOverridesForDateAsync(date))
                .GroupBy(o => o.DiscordId).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var h in hours)
            {
                var slotTpe = new DateTimeOffset(date.ToDateTime(new TimeOnly(h, 0)), TimeSpan.FromHours(8));
                if (slotTpe <= nowTpe)
                    continue;   // 過去時段不可開團

                var booked = bookings.GetValueOrDefault(slotTpe.ToUniversalTime(), []);
                var availableJobs = eligible
                    .Where(e => IsAvailableAt(e, weekday, new TimeOnly(h, 0), overridesByDiscord) && !booked.Contains(e.DiscordId))
                    .Select(e => e.Job)
                    .ToList();

                result.Cells.Add(new HeatmapCellDto
                {
                    SlotDateTime = slotTpe,
                    FilledCount = CompositionQuota.MaxRequirementSlotsFilled(availableJobs, requirements)
                });
            }
        }
        return result;
    }

    // 某整點 h:00 是否落在常設時段 [Start, End) 內（weekday-agnostic，供熱力圖決定顯示哪些整點）。
    // End=00:00 視為當日結束(24:00)；Start>End 視為跨午夜 wrap（涵蓋 h>=Start 或 h<End，如 22:00–02:00 → 含 00/01）。
    private static bool HourInWindow(int hour, PlayerAvailability a)
    {
        var t = hour * 60;
        var s = a.StartTime.Hour * 60 + a.StartTime.Minute;
        var e = a.EndTime is { Hour: 0, Minute: 0 } ? 24 * 60 : a.EndTime.Hour * 60 + a.EndTime.Minute;
        return s > e ? (t >= s || t < e) : (t >= s && t < e);
    }

    // 候選是否合格：符合至少一需求列（本王通關≥門檻 且 人物等級≥門檻 且 某可接受職業==角色職業 且 攻擊≥該職下限）。
    // 候選清單（GetCandidatesAsync）+ 招募熱力圖每格共用（leader-recruitment-heatmap），避免兩處過濾邏輯走鐘。
    private static bool IsCandidateEligible(CandidatePoolItem item, IReadOnlyCollection<TeamSlotRequirement> requirements)
        => requirements.Any(r =>
            item.BossClearCount >= r.MinClearCount &&
            item.Level >= r.MinLevel &&
            r.Jobs.Any(j => j.Job == item.Job && item.AttackPower >= j.MinAttackPower));

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
        await BumpActivityAsync(leaderDiscordId);   // 心跳：隊長邀請
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
        var memberId = await _memberRepository.CreateAsync(new TeamSlotCharacter
        {
            TeamSlotId = teamSlotId,
            DiscordId = character.DiscordId,
            DiscordName = "",
            CharacterId = character.Id,
            CharacterName = character.Name,
            Job = character.Job,
            AttackPower = character.AttackPower,
            Level = character.Level,
            Status = TeamSlotMemberStatus.Invited,
            SlotDateTime = team.SlotDateTime,
            IsManual = true
        });

        // roster + 被邀角色快照 → bot 用 embed 呈現，讓被邀玩家決定前看隊伍組成（bot-composed-embeds）。
        var embed = await BuildEmbedSnapshotAsync(team, teamSlotId);
        embed.SubjectName = character.Name;
        embed.SubjectJob = character.Job;
        embed.SubjectAttackPower = character.AttackPower;
        embed.SubjectLevel = character.Level;
        embed.SubjectMapleBlessingLevel = character.MapleBlessingLevel;

        // 通知被邀玩家：帶 InviteResponse + memberId + embed → bot 渲染成員 embed +「接受/拒絕」按鈕。
        await NotifyAsync(team.BossId, team.SlotDateTime, character.DiscordId, "/me/teams",
            (boss, time) => $"隊長邀請你加入「{boss}」{time} 的隊伍。",
            TeamNotificationAction.InviteResponse, memberId, embed);
    }

    public async Task AcceptInviteAsync(int memberId, ulong currentDiscordId)
    {
        await BumpActivityAsync(currentDiscordId);   // 心跳：玩家接受邀請
        var member = await _memberRepository.GetByIdAsync(memberId);
        if (member == null)
            throw new NotFoundException($"Invitation {memberId} not found");
        if (member.DiscordId != currentDiscordId)
            throw new ForbiddenException("只能接受自己的邀請。");
        if (member.Status != TeamSlotMemberStatus.Invited)
            throw new BusinessException("此邀請目前無法接受（狀態已變）。");

        await ConfirmMemberAsync(member);
        // 隊長不再逐筆收「有人接受」（大量邀請會淹沒）→ 改為額滿時通知一次（見 ConfirmMemberAsync）。
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
            await _teamSlotEditLock.AcquireTeamSlotEditLockAsync(member.TeamSlotId);
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

        // composition-quota：職業名額硬篩。本次定案後，已確認職業能否仍可行地指派到需求名額（含未指定池）？
        // 無需求列 → 無職業約束（沿用純容量把關）。在 advisory lock 內、與其他 confirm 序列化 → 兩人同職業同時接受、
        // 第二個重跑匹配看到不可行而擋。requirements 於此撈一次、下方撤邀重用。
        var requirements = (await _requirementRepository.GetByTeamSlotIdAsync(member.TeamSlotId)).ToList();
        if (requirements.Count > 0)
        {
            var tentativeJobs = (await _membershipQuery.GetConfirmedJobsAsync(member.TeamSlotId)).ToList();
            tentativeJobs.Add(member.Job);
            if (!CompositionQuota.IsFeasible(tentativeJobs, requirements, capacity))
                throw new BusinessException("此職業名額已滿。");
        }

        var ok = await _memberRepository.UpdateStatusAsync(member.Id!.Value, TeamSlotMemberStatus.Confirmed, member.Version!);
        if (!ok)
            throw new BusinessException("狀態已被更新，請重新整理。");

        // period-less §8 Phase 3：入隊後清掉該玩家的找隊意圖（已找到隊、不再掛看板）。scheduled accept 無意圖 → no-op。
        await _lfgIntentRepository.DeleteByDiscordIdAsync(member.DiscordId);

        // 定案後自動撤邀（皆在同一 advisory lock 內、與其他 confirm 序列化）：被撤者原邀請 DM 編輯成「已失效」+ 移按鈕
        // （dm-revoke-cleanup，消死按鈕；否則玩家端只剩無法按的死按鈕、隊長邀請數也虛掛）。
        if (await _memberRepository.CountConfirmedAsync(member.TeamSlotId) >= capacity)
        {
            // (1) 隊伍額滿 → 撤全部待接受邀請 + 只「一次」通知隊長滿員（取代逐筆「有人接受」）。
            var revoked = await _memberRepository.RevokePendingInvitesAsync(member.TeamSlotId);
            await NotifyAsync(team.BossId, team.SlotDateTime, team.LeaderDiscordId ?? 0, "/me/led-teams",
                (boss, time) => $"你的「{boss}」{time} 隊伍已滿員。");
            foreach (var r in revoked)
                if (r.DmMessageId is { } mid)   // id 未回寫（DM 尚未派發）→ 跳過清理，退回死按鈕
                    await EnqueueInviteRevokedCleanupAsync(r.DiscordId, mid, "此邀請已失效（隊伍已滿）。");
        }
        else if (requirements.Count > 0)
        {
            // (2) 未滿但某些職業名額已滿（composition-quota）→ 只撤「加入後不可行」職業的待接受邀請（保留 Pull 超額搶位）。
            var confirmedJobs = (await _membershipQuery.GetConfirmedJobsAsync(member.TeamSlotId)).ToList();
            var fullJobs = (await _membershipQuery.GetPendingInviteJobsAsync(member.TeamSlotId))
                .Where(j => !CompositionQuota.IsFeasible(confirmedJobs.Append(j).ToList(), requirements, capacity))
                .ToList();
            if (fullJobs.Count > 0)
            {
                var revoked = await _memberRepository.RevokePendingInvitesByJobsAsync(member.TeamSlotId, fullJobs);
                foreach (var r in revoked)
                    if (r.DmMessageId is { } mid)
                        await EnqueueInviteRevokedCleanupAsync(r.DiscordId, mid, "此邀請已失效（該職業名額已滿）。");
            }
        }
    }

    public async Task DeclineInviteAsync(int memberId, ulong currentDiscordId)
    {
        await BumpActivityAsync(currentDiscordId);   // 心跳：玩家婉拒（仍是 presence——他在、有回應）
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

        // 隊長 Pull 常大量邀請 →「被拒絕」對隊長是噪音，不 DM（member 標 Rejected，拒絕狀態 UI 可見即可）。
    }

    public async Task ApplyAsync(int teamSlotId, string characterId, ulong applicantDiscordId)
    {
        await BumpActivityAsync(applicantDiscordId);   // 心跳：玩家申請
        var team = await _teamSlotRepository.GetByIdAsync(teamSlotId);
        if (team == null)
            throw new NotFoundException($"TeamSlot {teamSlotId} not found");

        // 申請須用本人角色（存在 + 擁有權）→ 404；快照角色屬性（同邀請）。
        var character = await _characterQuery.GetByIdAsync(characterId);
        if (character == null || character.DiscordId != applicantDiscordId)
            throw new NotFoundException($"Character {characterId} not found");

        // 已在此隊 active（Confirmed/Invited/Applied）→ 擋。uq_tsc_active_membership 只擋 Applied/Invited 重複，
        // 擋不住「已 Confirmed 又來申請」→ 這裡補上不變式（前端尋隊也已排除，這是直打 API 的兜底）。
        var active = await _memberRepository.GetActiveMemberDiscordIdsAsync(teamSlotId);
        if (active.Contains(applicantDiscordId))
            throw new BusinessException("你已在此隊、或已有待處理的申請／邀請。");

        // 重複申請（同隊同人已有 Applied/Invited）另由 DB unique uq_tsc_active_membership → 23505 → 409 兜底。
        var memberId = await _memberRepository.CreateAsync(new TeamSlotCharacter
        {
            TeamSlotId = teamSlotId,
            DiscordId = applicantDiscordId,
            DiscordName = "",
            CharacterId = character.Id,
            CharacterName = character.Name,
            Job = character.Job,
            AttackPower = character.AttackPower,
            Level = character.Level,
            Status = TeamSlotMemberStatus.Applied,
            SlotDateTime = team.SlotDateTime,
            IsManual = true
        });

        // roster + 申請者能力快照 → bot 用 embed 呈現，讓隊長看申請者能力再核准（bot-composed-embeds）。
        var embed = await BuildEmbedSnapshotAsync(team, teamSlotId);
        embed.SubjectName = character.Name;
        embed.SubjectJob = character.Job;
        embed.SubjectAttackPower = character.AttackPower;
        embed.SubjectLevel = character.Level;
        embed.SubjectMapleBlessingLevel = character.MapleBlessingLevel;

        // 通知隊長有新申請：帶 ApplicationReview + memberId + embed → bot 渲染申請者能力 + roster + 核准/拒絕。
        await NotifyAsync(team.BossId, team.SlotDateTime, team.LeaderDiscordId ?? 0, $"/teams/{team.Id}/applications",
            (boss, time) => $"有玩家申請加入你「{boss}」{time} 的隊伍。",
            TeamNotificationAction.ApplicationReview, memberId, embed);
    }

    public async Task ApproveAsync(int memberId, ulong leaderDiscordId)
    {
        await BumpActivityAsync(leaderDiscordId);   // 心跳：隊長核准（申請者已在 Apply 時 bump 過）
        var member = await _memberRepository.GetByIdAsync(memberId);
        if (member == null)
            throw new NotFoundException($"Application {memberId} not found");
        if (member.Status != TeamSlotMemberStatus.Applied)
            throw new BusinessException("此申請目前無法核准（狀態已變）。");

        var team = await EnsureLeaderOwnsTeamAsync(member.TeamSlotId, leaderDiscordId, "只有隊長能核准申請。");
        await ConfirmMemberAsync(member);

        // 通知申請玩家：通過入隊
        await NotifyAsync(team.BossId, team.SlotDateTime, member.DiscordId, "/me/teams",
            (boss, time) => $"你申請的「{boss}」{time} 隊伍已通過、成功入隊。");
    }

    public async Task RejectAsync(int memberId, ulong leaderDiscordId)
    {
        await BumpActivityAsync(leaderDiscordId);   // 心跳：隊長拒絕申請
        var member = await _memberRepository.GetByIdAsync(memberId);
        if (member == null)
            throw new NotFoundException($"Application {memberId} not found");
        if (member.Status != TeamSlotMemberStatus.Applied)
            throw new BusinessException("此申請目前無法拒絕（狀態已變）。");

        await EnsureLeaderOwnsTeamAsync(member.TeamSlotId, leaderDiscordId, "只有隊長能拒絕申請。");

        var ok = await _memberRepository.UpdateStatusAsync(memberId, TeamSlotMemberStatus.Rejected, member.Version!);
        if (!ok)
            throw new BusinessException("狀態已被更新，請重新整理。");
        // 玩家申請多隊被拒會被淹沒 →「申請未通過」不 DM（member 標 Rejected，UI 可見即可）。
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

    public async Task<IEnumerable<ApplicantDto>> GetApplicationsAsync(int teamSlotId, ulong leaderDiscordId)
    {
        await EnsureLeaderOwnsTeamAsync(teamSlotId, leaderDiscordId, "只有隊長能查看申請。");
        return await _membershipQuery.GetApplicationsAsync(teamSlotId);
    }

    public async Task LeaveTeamAsync(int teamSlotId, ulong currentDiscordId)
    {
        await BumpActivityAsync(currentDiscordId);   // 心跳：退隊也是 deliberate 動作＝presence
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
            await NotifyAsync(team.BossId, team.SlotDateTime, team.LeaderDiscordId ?? 0, $"/teams/{team.Id}/candidates",
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
        // 帶 TransferResponse + teamSlotId + embed（roster，無主角）→ bot 渲染隊伍組成 +「接受/拒絕」，
        // 讓新隊長候選看隊伍再決定。
        var embed = await BuildEmbedSnapshotAsync(team, teamSlotId);
        await NotifyAsync(team.BossId, team.SlotDateTime, member.DiscordId, "/me/teams",
            (boss, time) => $"隊長想把「{boss}」{time} 的隊長轉給你。",
            TeamNotificationAction.TransferResponse, teamSlotId, embed);
    }

    public async Task RespondLeaderTransferAsync(int teamSlotId, ulong currentDiscordId, string action)
    {
        await BumpActivityAsync(currentDiscordId);   // 心跳：回應隊長轉讓
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
                await NotifyAsync(team.BossId, team.SlotDateTime, oldLeader, "/me/led-teams",
                    (boss, time) => $"你「{boss}」{time} 的隊長轉讓已被接受、對方成為新隊長。");
                break;
            case "decline":
                await _teamSlotRepository.SetPendingLeaderAsync(teamSlotId, null);
                await NotifyAsync(team.BossId, team.SlotDateTime, oldLeader, "/me/led-teams",
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
        return await _membershipQuery.GetRosterAsync(teamSlotId, leaderDiscordId);
    }

    public async Task<IEnumerable<TeamMemberDto>> GetTeamMembersAsync(int teamSlotId, ulong requesterDiscordId)
    {
        var team = await _teamSlotRepository.GetByIdAsync(teamSlotId);
        if (team == null)
            throw new NotFoundException($"TeamSlot {teamSlotId} not found");

        // 授權：本隊 Confirmed 成員、或隊長本人才可看組成（隊長可能只揪人、自己沒佔位）。
        var self = await _memberRepository.GetConfirmedMemberAsync(teamSlotId, requesterDiscordId);
        if (self == null && team.LeaderDiscordId != requesterDiscordId)
            throw new ForbiddenException("只有隊員能看隊伍組成。");

        return await _membershipQuery.GetConfirmedMembersAsync(teamSlotId);
    }

    public async Task<IEnumerable<RecruitmentGapRowDto>> GetRecruitmentGapAsync(int teamSlotId, ulong leaderDiscordId)
    {
        await EnsureLeaderOwnsTeamAsync(teamSlotId, leaderDiscordId, "只有隊長能查看招募缺口。");

        var requirements = (await _membershipQuery.GetRequirementsAsync(teamSlotId)).ToList();
        // 已 Confirmed 成員的職業當作「已填的格」（含隊長自帶的角色）；進隊即填該格，不再看攻擊/通關門檻。
        var confirmedJobs = (await _membershipQuery.GetConfirmedJobsAsync(teamSlotId)).ToList();

        // 逐列貪婪配對（軟提示，不強制組成）：先配「限定職業」的列、再配「不限職業」的列，
        // 讓專職成員優先填掉專職需求、剩的人才去補不限位。重疊只求近似、不上匈牙利。
        var pool = confirmedJobs.ToList();
        var result = new List<RecruitmentGapRowDto>();
        foreach (var req in requirements.OrderByDescending(r => r.Jobs.Count > 0))
        {
            var acceptable = req.Jobs.Select(j => j.Job).ToHashSet();
            var matched = 0;
            for (var i = pool.Count - 1; i >= 0 && matched < req.Count; i--)
            {
                if (acceptable.Count == 0 || acceptable.Contains(pool[i]))
                {
                    pool.RemoveAt(i);
                    matched++;
                }
            }
            result.Add(new RecruitmentGapRowDto
            {
                Jobs = acceptable.ToList(),
                Required = req.Count,
                Remaining = req.Count - matched
            });
        }
        return result;
    }
}
