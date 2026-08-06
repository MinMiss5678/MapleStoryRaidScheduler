using Application.DTOs;
using Application.Exceptions;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Services;

public class TeamLeaderService : ITeamLeaderService
{
    private readonly IBossRepository _bossRepository;
    private readonly IPeriodQuery _periodQuery;
    private readonly ITeamSlotRepository _teamSlotRepository;
    private readonly ITeamSlotRequirementRepository _requirementRepository;

    public TeamLeaderService(
        IBossRepository bossRepository,
        IPeriodQuery periodQuery,
        ITeamSlotRepository teamSlotRepository,
        ITeamSlotRequirementRepository requirementRepository)
    {
        _bossRepository = bossRepository;
        _periodQuery = periodQuery;
        _teamSlotRepository = teamSlotRepository;
        _requirementRepository = requirementRepository;
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
}
