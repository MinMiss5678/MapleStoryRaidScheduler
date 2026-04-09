using Application.DTOs;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Services;

public class RegisterService : IRegisterService
{
    private readonly IPeriodQuery _periodQuery;
    private readonly IPlayerRegisterRepository _playerRegisterRepository;
    private readonly IPlayerRegisterQuery _playerRegisterQuery;
    private readonly ICharacterRegisterRepository _characterRegisterRepository;
    private readonly IPlayerAvailabilityRepository _playerAvailabilityRepository;
    private readonly ITeamSlotAutoAssignService _autoAssignService;
    private readonly ITeamSlotCharacterRepository _teamSlotCharacterRepository;
    private readonly ISystemConfigService _systemConfigService;

    public RegisterService(IPeriodQuery periodQuery,
        IPlayerRegisterRepository playerRegisterRepository, IPlayerRegisterQuery playerRegisterQuery,
        ICharacterRegisterRepository characterRegisterRepository,
        IPlayerAvailabilityRepository playerAvailabilityRepository,
        ITeamSlotCharacterRepository teamSlotCharacterRepository,
        ITeamSlotAutoAssignService autoAssignService,
        ISystemConfigService systemConfigService)
    {
        _periodQuery = periodQuery;
        _playerRegisterRepository = playerRegisterRepository;
        _playerRegisterQuery = playerRegisterQuery;
        _characterRegisterRepository = characterRegisterRepository;
        _playerAvailabilityRepository = playerAvailabilityRepository;
        _autoAssignService = autoAssignService;
        _teamSlotCharacterRepository = teamSlotCharacterRepository;
        _systemConfigService = systemConfigService;
    }

    public async Task<RegisterDto?> GetAsync(ulong discordId)
    {
        var periodId = await _periodQuery.GetPeriodIdByNowAsync();
        if (periodId == 0) return null;
        return await GetByPeriodAsync(discordId, periodId);
    }

    public async Task<RegisterDto?> GetLastAsync(ulong discordId)
    {
        var periodId = await _periodQuery.GetLastPeriodIdAsync();
        if (periodId == 0) return null;
        return await GetByPeriodAsync(discordId, periodId);
    }

    private async Task<RegisterDto?> GetByPeriodAsync(ulong discordId, int periodId)
    {
        var registers = (await _playerRegisterRepository.GetListAsync(discordId, periodId)).ToList();
        var first = registers.FirstOrDefault();
        if (first == null)
            return null;

        var availabilities = await _playerAvailabilityRepository.GetByPlayerRegisterIdAsync(first.Id);

        var flat = new RegisterDto
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

        return flat;
    }
    
    public async Task<IEnumerable<TeamSlotCharacter>> GetByQueryAsync(RegisterGetByQueryRequest request)
    {
        var periodId = await _periodQuery.GetPeriodIdByDateAsync(request.SlotDateTime.Value);
            
        var registers = await _playerRegisterQuery.GetByQueryAsync(request, periodId);
        var first = registers.FirstOrDefault();
        if (first == null)
            return new List<TeamSlotCharacter>();
        
        var flat = registers.Select(x => new TeamSlotCharacter()
        {
            DiscordId =  x.DiscordId,
            DiscordName = x.DiscordName,
            CharacterId = x.CharacterId,
            CharacterName = x.CharacterName,
            Job = x.Job,
            AttackPower = x.AttackPower,
            Rounds = x.Rounds
        });
        
        return flat;
    }

    public async Task CreateAsync(Register register)
    {
        await EnsureRegistrationOpen();

        var playRegisterId = await _playerRegisterRepository.CreateAsync(register);

        foreach (var availability in register.Availabilities)
        {
            await _playerAvailabilityRepository.CreateAsync(new PlayerAvailability
            {
                PlayerRegisterId = playRegisterId,
                Weekday = availability.Weekday,
                StartTime = availability.StartTime,
                EndTime = availability.EndTime
            });
        }

        foreach (var characterRegister in register.CharacterRegisters)
        {
            characterRegister.PlayerRegisterId = playRegisterId;
            await _characterRegisterRepository.CreateAsync(characterRegister);
        }

        await _autoAssignService.AutoAssignAsync(register);
    }

    public async Task UpdateAsync(RegisterUpdateCommand command)
    {
        await EnsureRegistrationOpen();

        var register = new Register
        {
            Id = command.Id,
            DiscordId = command.DiscordId,
            PeriodId = command.PeriodId,
            CharacterRegisters = command.CharacterRegisters,
            Availabilities = command.Availabilities
        };

        await _playerRegisterRepository.UpdateAsync(register);

        await _playerAvailabilityRepository.DeleteByPlayerRegisterIdAsync(command.Id);
        foreach (var availability in command.Availabilities)
        {
            await _playerAvailabilityRepository.CreateAsync(new PlayerAvailability
            {
                PlayerRegisterId = command.Id,
                Weekday = availability.Weekday,
                StartTime = availability.StartTime,
                EndTime = availability.EndTime
            });
        }

        foreach (var c in command.DeleteCharacterRegisterIds)
        {
            await _characterRegisterRepository.DeleteAsync(c);
        }

        foreach (var characterRegister in command.CharacterRegisters)
        {
            if (characterRegister.Id != null)
            {
                await _characterRegisterRepository.UpdateAsync(characterRegister);
            }
            else
            {
                characterRegister.PlayerRegisterId = command.Id;

                await _characterRegisterRepository.CreateAsync(characterRegister);
            }
        }
    }

    private async Task EnsureRegistrationOpen()
    {
        var config = await _systemConfigService.GetAsync();
        var currentPeriod = await _periodQuery.GetByNowAsync();
        if (currentPeriod != null)
        {
            var deadline = config.GetDeadlineForPeriod(currentPeriod.StartDate);
            if (DateTimeOffset.Now > deadline)
            {
                throw new InvalidOperationException("目前已超過報名截止時間。");
            }
        }
    }

    public async Task DeleteAsync(ulong discordId, int id)
    {
        var period = await _periodQuery.GetByNowAsync();
        if (period == null) return;
        await _teamSlotCharacterRepository.DeleteByDiscordIdAndPeriodAsync(discordId, period.StartDate,
            period.EndDate);
        await _characterRegisterRepository.DeleteByPlayerRegisterIdAsync(id);
        await _playerRegisterRepository.DeleteAsync(discordId, id);
    }
}