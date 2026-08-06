using Application.DTOs;
using Application.Exceptions;
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
    private readonly ITeamSlotCharacterRepository _teamSlotCharacterRepository;
    private readonly ISystemConfigService _systemConfigService;
    private readonly IBossRepository _bossRepository;
    private readonly ICharacterQuery _characterQuery;

    public RegisterService(
        IPeriodQuery periodQuery,
        IPlayerRegisterRepository playerRegisterRepository,
        ICharacterRegisterRepository characterRegisterRepository,
        IPlayerAvailabilityRepository playerAvailabilityRepository,
        ITeamSlotCharacterRepository teamSlotCharacterRepository,
        ISystemConfigService systemConfigService,
        IBossRepository bossRepository,
        ICharacterQuery characterQuery)
    {
        _periodQuery = periodQuery;
        _playerRegisterRepository = playerRegisterRepository;
        _characterRegisterRepository = characterRegisterRepository;
        _playerAvailabilityRepository = playerAvailabilityRepository;
        _teamSlotCharacterRepository = teamSlotCharacterRepository;
        _systemConfigService = systemConfigService;
        _bossRepository = bossRepository;
        _characterQuery = characterQuery;
    }

    // CharacterRegister.CharacterId 的 FK（+擁有權）前線檢查：報名的角色必須是呼叫者本人的角色，
    // 否則轉 404——同時擋「角色不存在」（FK 後防的預期壞輸入）與「冒用他人角色 id」（IDOR）。
    // 見 plans/2026-08-06-validation-layering.md §2（FK → app 存在性檢查 → 404，DB FK 只當後防）。
    private async Task EnsureCharactersOwnedAsync(Register register)
    {
        if (register.CharacterRegisters.Count == 0) return;

        var ownedIds = (await _characterQuery.GetByDiscordIdAsync(register.DiscordId))
            .Select(c => c.Id).ToHashSet();

        var alien = register.CharacterRegisters.FirstOrDefault(c => !ownedIds.Contains(c.CharacterId));
        if (alien != null)
            throw new NotFoundException($"Character {alien.CharacterId} not found");
    }

    // 載入各 Boss 的 RoundConsumption 注入 domain，讓 Register 聚合自己守「每週場次預算」不變式
    // （domain 純粹、不碰 repository；與 TeamSlot.Capacity 由 service 注入同一模式）。
    // 同一份 Boss 清單順便當 CharacterRegister.BossId 的存在性檢查來源：預期內的壞 BossId 轉 404，
    // DB 的 FK 只當後防（打到 FK＝有路徑漏驗＝bug）。
    private async Task ValidateBossesAndBudgetAsync(Register register)
    {
        var bosses = (await _bossRepository.GetAllAsync()).ToDictionary(b => b.Id);

        var unknownBoss = register.CharacterRegisters.FirstOrDefault(c => !bosses.ContainsKey(c.BossId));
        if (unknownBoss != null)
            throw new NotFoundException($"Boss {unknownBoss.BossId} not found");

        register.EnsureRoundsWithinBudget(bosses.ToDictionary(kv => kv.Key, kv => kv.Value.RoundConsumption));
    }

    public async Task CreateAsync(RegisterCreateCommand command)
    {
        await EnsureRegistrationOpen();

        // 先驗 Period 存在，否則 PeriodId 不存在時 INSERT PlayerRegister 會 FK 違反 → 500（回乾淨 404）
        if (await _periodQuery.GetByIdAsync(command.PeriodId) == null)
            throw new NotFoundException($"Period {command.PeriodId} not found");

        if (await _playerRegisterRepository.ExistAsync(command.DiscordId, command.PeriodId))
            throw new BusinessException("您已完成本期報名，請勿重複提交。");

        // DTO → Entity mapping 在 Infrastructure 層完成
        var register = new Register
        {
            DiscordId = command.DiscordId,
            PeriodId = command.PeriodId,
            CharacterRegisters = command.CharacterRegisters.Select(c => new CharacterRegister
            {
                CharacterId = c.CharacterId ?? string.Empty,
                BossId = c.BossId ?? 0,
                Rounds = c.Rounds ?? 0
            }).ToList(),
            Availabilities = command.Availabilities.Select(a => new PlayerAvailability
            {
                Weekday = a.Weekday,
                StartTime = a.StartTime,
                EndTime = a.EndTime
            }).ToList()
        };

        await EnsureCharactersOwnedAsync(register);
        await ValidateBossesAndBudgetAsync(register);

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

        // leader-led（§7）：報名不再觸發自動排團——報名 = 只把角色+時段放進候選池，
        // 由隊長開隊後對候選池挑人（Pull）。自動排團引擎降級為隊長 auto-fill（Phase 3）。
    }

    public async Task UpdateAsync(RegisterUpdateCommand command)
    {
        await EnsureRegistrationOpen();

        // 不信任前端傳的 command.Id：改由 (discordId, periodId) 查出呼叫者自己的 registerId，
        // 後面所有子資源都用這個 id，避免玩家傳別人的 registerId 竄改/刪除別人的資料（IDOR）
        var registerId = await _playerRegisterRepository.GetIdAsync(command.DiscordId, command.PeriodId);
        if (registerId == null)
            throw new BusinessException("找不到本期報名，無法更新。");

        // DTO → Entity mapping 在 Infrastructure 層完成
        var charRegisters = command.CharacterRegisters.Select(c => new CharacterRegister
        {
            Id = c.Id,
            PlayerRegisterId = registerId.Value,
            CharacterId = c.CharacterId ?? string.Empty,
            BossId = c.BossId ?? 0,
            Rounds = c.Rounds ?? 0
        }).ToList();

        var register = new Register
        {
            Id = registerId.Value,
            DiscordId = command.DiscordId,
            PeriodId = command.PeriodId,
            CharacterRegisters = charRegisters,
            Availabilities = command.Availabilities.Select(a => new PlayerAvailability
            {
                Weekday = a.Weekday,
                StartTime = a.StartTime,
                EndTime = a.EndTime
            }).ToList()
        };

        await EnsureCharactersOwnedAsync(register);
        await ValidateBossesAndBudgetAsync(register);

        await _playerRegisterRepository.UpdateAsync(register);

        await _playerAvailabilityRepository.DeleteByPlayerRegisterIdAsync(registerId.Value);
        foreach (var availability in register.Availabilities)
        {
            await _playerAvailabilityRepository.CreateAsync(new PlayerAvailability
            {
                PlayerRegisterId = registerId.Value,
                Weekday = availability.Weekday,
                StartTime = availability.StartTime,
                EndTime = availability.EndTime
            });
        }

        foreach (var c in command.DeleteCharacterRegisterIds)
        {
            await _characterRegisterRepository.DeleteAsync(c, registerId.Value);
        }

        foreach (var characterRegister in charRegisters)
        {
            if (characterRegister.Id != null)
            {
                await _characterRegisterRepository.UpdateAsync(characterRegister);
            }
            else
            {
                await _characterRegisterRepository.CreateAsync(characterRegister);
            }
        }
    }

    private async Task EnsureRegistrationOpen()
    {
        var config = await _systemConfigService.GetAsync();
        var latestPeriod = await _periodQuery.GetActivePeriodAsync();
        if (latestPeriod != null)
        {
            var deadline = config.GetDeadlineForPeriod(latestPeriod.StartDate);
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new BusinessException("目前已超過報名截止時間。");
            }
        }
    }

    public async Task DeleteAsync(ulong discordId, int id)
    {
        var period = await _periodQuery.GetActivePeriodAsync();
        if (period == null) return;
        await _teamSlotCharacterRepository.DeleteByDiscordIdAndPeriodAsync(discordId, period.StartDate,
            period.EndDate);
        await _characterRegisterRepository.DeleteByPlayerRegisterIdAsync(id);
        await _playerRegisterRepository.DeleteAsync(discordId, id);
    }
}
