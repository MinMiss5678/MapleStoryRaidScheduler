using Application.DTOs;
using Application.Exceptions;
using Application.Interface;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Services;

public class AvailabilityOverrideService : IAvailabilityOverrideService
{
    private readonly IPlayerAvailabilityOverrideRepository _repository;

    public AvailabilityOverrideService(IPlayerAvailabilityOverrideRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<AvailabilityOverrideDto>> GetMineAsync(ulong discordId)
    {
        var rows = await _repository.GetByDiscordIdAsync(discordId);
        return rows.Select(o => new AvailabilityOverrideDto
        {
            Id = o.Id,
            Date = o.Date,
            StartTime = o.StartTime,
            EndTime = o.EndTime,
            IsAvailable = o.IsAvailable
        });
    }

    public async Task AddAsync(AvailabilityOverrideCreateCommand command)
    {
        // 前線驗：時段窗要有效（EndTime 00:00 視為整天；相等且非整天 → 空窗）。
        if (command.EndTime != new TimeOnly(0, 0) && command.StartTime >= command.EndTime)
            throw new BusinessException("結束時間必須晚於開始時間。");

        await _repository.CreateAsync(new PlayerAvailabilityOverride
        {
            DiscordId = command.DiscordId,
            Date = command.Date,
            StartTime = command.StartTime,
            EndTime = command.EndTime,
            IsAvailable = command.IsAvailable
        });
    }

    public Task RemoveAsync(ulong discordId, int id) => _repository.DeleteAsync(discordId, id);
}
