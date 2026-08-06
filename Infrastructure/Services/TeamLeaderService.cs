using Application.DTOs;
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
    private readonly IPeriodQuery _periodQuery;
    private readonly ITeamSlotRepository _teamSlotRepository;
    private readonly ITeamSlotRequirementRepository _requirementRepository;
    private readonly ITeamCandidateQuery _candidateQuery;
    private readonly ITeamSlotCharacterRepository _memberRepository;
    private readonly ICharacterQuery _characterQuery;
    private readonly IRegistrationLock _registrationLock;

    public TeamLeaderService(
        IBossRepository bossRepository,
        IPeriodQuery periodQuery,
        ITeamSlotRepository teamSlotRepository,
        ITeamSlotRequirementRepository requirementRepository,
        ITeamCandidateQuery candidateQuery,
        ITeamSlotCharacterRepository memberRepository,
        ICharacterQuery characterQuery,
        IRegistrationLock registrationLock)
    {
        _bossRepository = bossRepository;
        _periodQuery = periodQuery;
        _teamSlotRepository = teamSlotRepository;
        _requirementRepository = requirementRepository;
        _candidateQuery = candidateQuery;
        _memberRepository = memberRepository;
        _characterQuery = characterQuery;
        _registrationLock = registrationLock;
    }

    public async Task<int> CreateTeamAsync(CreateTeamCommand command)
    {
        // Boss FK 前線檢查 → 404（見 plans/2026-08-06-validation-layering.md §2）
        if (await _bossRepository.GetByIdAsync(command.BossId) == null)
            throw new NotFoundException($"Boss {command.BossId} not found");

        // 週期權威歸屬：由 SlotDateTime 落點解析 PeriodId，守 §3 硬綁不變式「SlotDateTime ∈ 該 Period 區間」。
        // 查無（回 0）＝開隊時間不在任何開放週期 → 400。
        var periodId = await _periodQuery.GetPeriodIdByDateAsync(command.SlotDateTime);
        if (periodId == 0)
            throw new BusinessException("開隊時間不在任何開放週期內。");

        var teamSlotId = await _teamSlotRepository.CreateAsync(new TeamSlot
        {
            BossId = command.BossId,
            PeriodId = periodId,
            SlotDateTime = command.SlotDateTime,
            Source = TeamSlotSource.Leader,
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

        // period 由 SlotDateTime 解析（免讀 TeamSlot.PeriodId；§3 不變式保證兩者一致）。查無 → 無候選。
        var periodId = await _periodQuery.GetPeriodIdByDateAsync(team.SlotDateTime);
        var period = periodId == 0 ? null : await _periodQuery.GetByIdAsync(periodId);
        if (period == null)
            return [];

        var requirements = (await _requirementRepository.GetByTeamSlotIdAsync(teamSlotId)).ToList();
        var pool = await _candidateQuery.GetPoolAsync(periodId, team.BossId);

        // 團時間 → weekday/time（TPE），比照 TeamSlotAutoAssignService 慣例
        var twTime = team.SlotDateTime.ToOffset(TimeSpan.FromHours(8));
        int teamWeekday = SlotDateCalculator.ToIsoWeekday(twTime.DayOfWeek);
        var teamTime = TimeOnly.FromDateTime(twTime.DateTime);

        return pool
            .Where(item =>
                // 時段重疊：任一時段涵蓋團時間
                item.Availabilities.Any(a => SlotDateCalculator.IsTimeInAvailability(teamWeekday, teamTime, a, period))
                // 且符合至少一需求列：某可接受職業==角色職業 且 攻擊≥該職下限 且 本王通關≥該列門檻。
                // 無需求列 → 無候選（隊長須先定義條件才看得到候選）。
                && requirements.Any(r =>
                    item.BossClearCount >= r.MinClearCount &&
                    r.Jobs.Any(j => j.Job == item.Job && item.AttackPower >= j.MinAttackPower)))
            .Select(item => new TeamCandidateDto
            {
                CharacterId = item.CharacterId,
                CharacterName = item.CharacterName,
                Job = item.Job,
                AttackPower = item.AttackPower,
                MapleBlessingLevel = item.MapleBlessingLevel,
                BossClearCount = item.BossClearCount
            })
            .ToList();
    }

    public async Task InviteMemberAsync(int teamSlotId, string characterId, ulong leaderDiscordId)
    {
        var team = await _teamSlotRepository.GetByIdAsync(teamSlotId);
        if (team == null)
            throw new NotFoundException($"TeamSlot {teamSlotId} not found");
        if (team.LeaderDiscordId != leaderDiscordId)
            throw new ForbiddenException("只有隊長能邀請成員。");

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

        // 悲觀鎖（per-team）：取鎖 → 重讀 Confirmed 數 → 檢查容量 → 改狀態。序列化同隊的多方 confirm，防超編。
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
        var confirmed = await _memberRepository.CountConfirmedAsync(member.TeamSlotId);
        if (confirmed >= capacity)
            throw new BusinessException("隊伍已滿。");

        // 跨隊時段重疊：改成 Confirmed 使該列進入 uq_tsc_confirmed_overlap；若同玩家同時段已有 Confirmed
        // → 23505 → ExceptionHandlerMiddleware 轉 409（見 plans/2026-08-06-validation-layering.md §4）。
        var ok = await _memberRepository.UpdateStatusAsync(memberId, TeamSlotMemberStatus.Confirmed, member.Version!);
        if (!ok)
            throw new BusinessException("邀請狀態已被更新，請重新整理。");
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
    }
}
