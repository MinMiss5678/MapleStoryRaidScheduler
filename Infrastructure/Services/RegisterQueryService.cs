using Application.DTOs;
using Application.Exceptions;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Services;

public class RegisterQueryService : IRegisterQueryService
{
    private readonly IPeriodQuery _periodQuery;
    private readonly IPlayerRegisterRepository _playerRegisterRepository;
    private readonly IPlayerRegisterQuery _playerRegisterQuery;
    private readonly IPlayerAvailabilityRepository _playerAvailabilityRepository;

    public RegisterQueryService(
        IPeriodQuery periodQuery,
        IPlayerRegisterRepository playerRegisterRepository,
        IPlayerRegisterQuery playerRegisterQuery,
        IPlayerAvailabilityRepository playerAvailabilityRepository)
    {
        _periodQuery = periodQuery;
        _playerRegisterRepository = playerRegisterRepository;
        _playerRegisterQuery = playerRegisterQuery;
        _playerAvailabilityRepository = playerAvailabilityRepository;
    }

    public async Task<RegisterDto> GetAsync(ulong discordId)
    {
        var periodId = await _periodQuery.GetPeriodIdByNowAsync();
        if (periodId == 0) throw new NotFoundException("No active period found");
        return await GetByPeriodAsync(discordId, periodId);
    }

    public async Task<RegisterDto> GetLastAsync(ulong discordId)
    {
        var periodId = await _periodQuery.GetLastPeriodIdAsync();
        if (periodId == 0) throw new NotFoundException("No last period found");
        return await GetByPeriodAsync(discordId, periodId);
    }

    private async Task<RegisterDto> GetByPeriodAsync(ulong discordId, int periodId)
    {
        var registers = (await _playerRegisterRepository.GetListAsync(discordId, periodId)).ToList();
        var first = registers.FirstOrDefault();
        if (first == null)
            throw new NotFoundException("Register not found");

        var availabilities = await _playerAvailabilityRepository.GetByPlayerRegisterIdAsync(first.Id);

        return new RegisterDto
        {
            Id = first.Id,
            PeriodId = first.PeriodId,
            Availabilities = availabilities.Select(a => new PlayerAvailability
            {
                Weekday = a.Weekday,
                StartTime = a.StartTime,
                EndTime = a.EndTime
            }).ToList(),
            CharacterRegisters = registers
                .Where(r => r.CharacterRegisterId != null)
                .Select(r => new CharacterRegisterDto
                {
                    Id = r.CharacterRegisterId,
                    CharacterId = r.CharacterId,
                    BossId = r.BossId,
                    Rounds = r.Rounds
                })
                .ToList()
        };
    }

    public async Task<IEnumerable<TeamSlotCharacter>> GetByQueryAsync(RegisterGetByQueryRequest request)
    {
        var periodId = await _periodQuery.GetPeriodIdByDateAsync(request.SlotDateTime.Value);
        var registers = await _playerRegisterQuery.GetByQueryAsync(request, periodId);

        return registers.Select(x => new TeamSlotCharacter
        {
            DiscordId = x.DiscordId,
            DiscordName = x.DiscordName,
            CharacterId = x.CharacterId,
            CharacterName = x.CharacterName,
            Job = x.Job,
            AttackPower = x.AttackPower,
            Rounds = x.Rounds
        });
    }
}
