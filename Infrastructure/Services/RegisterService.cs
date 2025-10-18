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
    private readonly IUnitOfWork _unitOfWork;

    public RegisterService(IPeriodQuery periodQuery,
        IPlayerRegisterRepository playerRegisterRepository, IPlayerRegisterQuery playerRegisterQuery,
        ICharacterRegisterRepository characterRegisterRepository, IUnitOfWork unitOfWork)
    {
        _periodQuery = periodQuery;
        _playerRegisterRepository = playerRegisterRepository;
        _playerRegisterQuery = playerRegisterQuery;
        _characterRegisterRepository = characterRegisterRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<RegisterDto> GetAsync(ulong discordId)
    {
        var periodId = await _periodQuery.GetPeriodIdByNowAsync();
        var registers = await _playerRegisterRepository.GetListAsync(discordId, periodId);
        var first = registers.FirstOrDefault();
        if (first == null)
            return null;

        var flat = new RegisterDto
        {
            Id = first.Id,
            PeriodId = first.PeriodId,
            Weekdays = first.Weekdays,
            Timeslots = first.Timeslots,
            CharacterRegisters = registers
                .Where(r => r.CharacterRegisterId != null)
                .Select(r => new CharacterRegisterDto
                {
                    Id = r.CharacterRegisterId,
                    CharacterId = r.CharacterId,
                    Job = r.Job,
                    BossId = r.BossId,
                    Rounds = r.Rounds
                })
                .ToList()
        };

        return flat;
    }
    
    public async Task<IEnumerable<TeamSlotCharacter>> GetByQueryAsync(RegisterGetByQueryRequest request)
    {
        var periodId = await _periodQuery.GetPeriodIdByNowAsync();
        var registers = await _playerRegisterQuery.GetByQueryAsync(request, periodId);
        var first = registers.FirstOrDefault();
        if (first == null)
            return null;

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
        await _unitOfWork.BeginTransactionAsync();
        try
        {
            var playRegisterId = await _playerRegisterRepository.CreateAsync(register);
            foreach (var characterRegister in register.CharacterRegisters)
            {
                characterRegister.PlayerRegisterId = playRegisterId;
                await _characterRegisterRepository.CreateAsync(characterRegister);
            }

            await _unitOfWork.CommitAsync();
        }
        catch (Exception e)
        {
            await _unitOfWork.RollbackAsync();
            throw;
        }
    }

    public async Task UpdateAsync(Register register)
    {
        await _unitOfWork.BeginTransactionAsync();
        try
        {
            await _playerRegisterRepository.UpdateAsync(register);
            
            foreach (var c in register.DeleteCharacterRegisterIds)
            {
                await _characterRegisterRepository.DeleteAsync(c);
            }

            foreach (var characterRegister in register.CharacterRegisters)
            {
                if (characterRegister.Id != null)
                {
                    await _characterRegisterRepository.UpdateAsync(characterRegister);
                }
                else
                {
                    characterRegister.PlayerRegisterId = register.Id;

                    await _characterRegisterRepository.CreateAsync(characterRegister);
                }
            }

            await _unitOfWork.CommitAsync();
        }
        catch (Exception e)
        {
            await _unitOfWork.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteAsync(ulong discordId, int id)
    {
        await _unitOfWork.BeginTransactionAsync();
        try
        {
            await _characterRegisterRepository.DeleteByPlayerRegisterIdAsync(id);
            await _playerRegisterRepository.DeleteAsync(discordId, id);

            await _unitOfWork.CommitAsync();
        }
        catch (Exception e)
        {
            await _unitOfWork.RollbackAsync();
            throw;
        }
    }
}