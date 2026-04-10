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
    private readonly ICharacterRegisterRepository _characterRegisterRepository;
    private readonly IPlayerAvailabilityRepository _playerAvailabilityRepository;
    private readonly ITeamSlotAutoAssignService _autoAssignService;
    private readonly ITeamSlotCharacterRepository _teamSlotCharacterRepository;
    private readonly ISystemConfigService _systemConfigService;

    public RegisterService(
        IPeriodQuery periodQuery,
        IPlayerRegisterRepository playerRegisterRepository,
        ICharacterRegisterRepository characterRegisterRepository,
        IPlayerAvailabilityRepository playerAvailabilityRepository,
        ITeamSlotCharacterRepository teamSlotCharacterRepository,
        ITeamSlotAutoAssignService autoAssignService,
        ISystemConfigService systemConfigService)
    {
        _periodQuery = periodQuery;
        _playerRegisterRepository = playerRegisterRepository;
        _characterRegisterRepository = characterRegisterRepository;
        _playerAvailabilityRepository = playerAvailabilityRepository;
        _autoAssignService = autoAssignService;
        _teamSlotCharacterRepository = teamSlotCharacterRepository;
        _systemConfigService = systemConfigService;
    }

    public async Task CreateAsync(Register register)
    {
        await EnsureRegistrationOpen();

        if (await _playerRegisterRepository.ExistAsync(register.DiscordId, register.PeriodId))
            throw new InvalidOperationException("您已完成本期報名，請勿重複提交。");

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