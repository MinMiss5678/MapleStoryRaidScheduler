using Application.DTOs;
using Application.Exceptions;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
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

    public TeamLeaderService(
        IBossRepository bossRepository,
        IPeriodQuery periodQuery,
        ITeamSlotRepository teamSlotRepository,
        ITeamSlotRequirementRepository requirementRepository,
        ITeamCandidateQuery candidateQuery)
    {
        _bossRepository = bossRepository;
        _periodQuery = periodQuery;
        _teamSlotRepository = teamSlotRepository;
        _requirementRepository = requirementRepository;
        _candidateQuery = candidateQuery;
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
}
