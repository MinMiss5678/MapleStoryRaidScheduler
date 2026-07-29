using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Helpers;
using Domain.Repositories;

namespace Infrastructure.Services;

public class TeamSlotAutoAssignService : ITeamSlotAutoAssignService
{
    private readonly ITeamSlotRepository _teamSlotRepository;
    private readonly ITeamSlotCharacterRepository _teamSlotCharacterRepository;
    private readonly IPeriodQuery _periodQuery;
    private readonly ICharacterQuery _characterQuery;
    private readonly IBossRepository _bossRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly ITeamSlotMergeService _mergeService;
    private readonly IRegistrationLock _registrationLock;

    public TeamSlotAutoAssignService(
        ITeamSlotRepository teamSlotRepository,
        ITeamSlotCharacterRepository teamSlotCharacterRepository,
        IPeriodQuery periodQuery,
        ICharacterQuery characterQuery,
        IBossRepository bossRepository,
        IPlayerRepository playerRepository,
        ITeamSlotMergeService mergeService,
        IRegistrationLock registrationLock)
    {
        _teamSlotRepository = teamSlotRepository;
        _teamSlotCharacterRepository = teamSlotCharacterRepository;
        _periodQuery = periodQuery;
        _characterQuery = characterQuery;
        _bossRepository = bossRepository;
        _playerRepository = playerRepository;
        _mergeService = mergeService;
        _registrationLock = registrationLock;
    }

    public async Task AutoAssignAsync(Register register)
    {
        // 併發防護：序列化同一 period 的自動分配，避免兩人同時報名各開一隊（read-then-write race）。
        // 取在最前面（讀隊伍之前），鎖隨 UoW 交易結束自動釋放。
        await _registrationLock.AcquireAutoAssignLockAsync(register.PeriodId);

        var period = await _periodQuery.GetByIdAsync(register.PeriodId);
        if (period == null) return;

        var teamSlots = (await _teamSlotRepository.GetByPeriodIdAsync(register.PeriodId)).ToList();
        var characters = await _characterQuery.GetByDiscordIdAsync(register.DiscordId);
        var player = await _playerRepository.GetAsync(register.DiscordId);

        // 載入各王容量一次，填到每個 TeamSlot 上——聚合的 HasRoom/AddMember 不變式靠 Capacity 才守得住。
        var bosses = (await _bossRepository.GetAllAsync()).ToList();
        int CapacityOf(int bossId) => bosses.FirstOrDefault(b => b.Id == bossId)?.RequireMembers ?? 6;
        foreach (var ts in teamSlots)
            ts.Capacity = CapacityOf(ts.BossId);

        foreach (var cr in register.CharacterRegisters)
        {
            var character = characters.FirstOrDefault(x => x.Id == cr.CharacterId);
            if (character == null || IsAlreadyAssigned(teamSlots, character.Id))
                continue;

            var matchingTeam = FindMatchingTeam(teamSlots, cr.BossId, register, period);

            if (matchingTeam != null)
            {
                var newMember = new TeamSlotCharacter { TeamSlotId = matchingTeam.Id, DiscordName = "", Job = "" };
                FillSlot(newMember, register, character, cr, player);
                matchingTeam.AddMember(newMember);   // 聚合守不變式（不超員/不重複）再持久化
                await _teamSlotCharacterRepository.CreateAsync(newMember);
            }
            else if (register.Availabilities.Any())
            {
                var newTeam = await CreateNewTeamAsync(register, cr, character, player, period, CapacityOf(cr.BossId));
                teamSlots.Add(newTeam);
            }
        }

        await _mergeService.MergeTeamsAsync(register);
    }

    private static bool IsAlreadyAssigned(List<TeamSlot> teamSlots, string characterId)
    {
        return teamSlots.Any(ts => ts.Contains(characterId));
    }

    private static TeamSlot? FindMatchingTeam(
        List<TeamSlot> teamSlots,
        int bossId,
        Register register,
        Period period)
    {
        return teamSlots
            .Where(ts => ts.BossId == bossId)
            .Where(ts => ts.HasRoom)
            .FirstOrDefault(ts =>
            {
                var twTime = ts.SlotDateTime.ToOffset(TimeSpan.FromHours(8));

                int weekday = SlotDateCalculator.ToIsoWeekday(twTime.DayOfWeek);
                var time = TimeOnly.FromDateTime(twTime.DateTime);

                return register.Availabilities.Any(a => SlotDateCalculator.IsTimeInAvailability(weekday, time, a, period));
            });
    }

    private static void FillSlot(
        TeamSlotCharacter slot,
        Register register,
        Character character,
        CharacterRegister cr,
        Player? player,
        bool isManual = false)
    {
        slot.DiscordId = register.DiscordId;
        slot.DiscordName = player?.DiscordName ?? "-";
        slot.CharacterId = character.Id;
        slot.CharacterName = character.Name;
        slot.Job = character.Job;
        slot.AttackPower = character.AttackPower;
        slot.Rounds = cr.Rounds;
        slot.IsManual = isManual;
    }

    private async Task<TeamSlot> CreateNewTeamAsync(
        Register register,
        CharacterRegister cr,
        Character character,
        Player? player,
        Period period,
        int capacity)
    {
        var targetAvail = SlotDateCalculator.GetBestAvailability(register, period);
        var targetDateTime = SlotDateCalculator.GetNextSlotDate(targetAvail, period);

        var teamSlot = new TeamSlot
        {
            BossId = cr.BossId,
            SlotDateTime = new DateTimeOffset(targetDateTime, TimeSpan.FromHours(8)).ToOffset(TimeSpan.Zero),
            Source = TeamSlotSource.Auto,   // 玩家報名觸發的系統自動隊
            Capacity = capacity,
        };

        var teamSlotId = await _teamSlotRepository.CreateAsync(teamSlot);

        var firstMember = new TeamSlotCharacter { TeamSlotId = teamSlotId, DiscordName = "", Job = "" };
        FillSlot(firstMember, register, character, cr, player);
        await _teamSlotCharacterRepository.CreateAsync(firstMember);

        teamSlot.Id = teamSlotId;
        return teamSlot;
    }
}
