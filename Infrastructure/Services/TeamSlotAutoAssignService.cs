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

    public TeamSlotAutoAssignService(
        ITeamSlotRepository teamSlotRepository,
        ITeamSlotCharacterRepository teamSlotCharacterRepository,
        IPeriodQuery periodQuery,
        ICharacterQuery characterQuery,
        IBossRepository bossRepository,
        IPlayerRepository playerRepository,
        ITeamSlotMergeService mergeService)
    {
        _teamSlotRepository = teamSlotRepository;
        _teamSlotCharacterRepository = teamSlotCharacterRepository;
        _periodQuery = periodQuery;
        _characterQuery = characterQuery;
        _bossRepository = bossRepository;
        _playerRepository = playerRepository;
        _mergeService = mergeService;
    }

    public async Task AutoAssignAsync(Register register)
    {
        var period = await _periodQuery.GetByIdAsync(register.PeriodId);
        if (period == null) return;

        var teamSlots = (await _teamSlotRepository.GetByPeriodIdAsync(register.PeriodId)).ToList();
        var characters = await _characterQuery.GetByDiscordIdAsync(register.DiscordId);
        var player = await _playerRepository.GetAsync(register.DiscordId);

        foreach (var cr in register.CharacterRegisters)
        {
            var character = characters.FirstOrDefault(x => x.Id == cr.CharacterId);
            if (character == null || IsAlreadyAssigned(teamSlots, character.Id))
                continue;

            var matchingTeam = await FindMatchingTeam(teamSlots, cr.BossId, register, period);

            if (matchingTeam != null)
            {
                var newMember = new TeamSlotCharacter { TeamSlotId = matchingTeam.Id };
                FillSlot(newMember, register, character, cr, player);
                await _teamSlotCharacterRepository.CreateAsync(newMember);
                matchingTeam.Characters.Add(newMember);
            }
            else if (register.Availabilities.Any())
            {
                var newTeam = await CreateNewTeamAsync(register, cr, character, player, period);
                teamSlots.Add(newTeam);
            }
        }

        await _mergeService.MergeTeamsAsync(register);
    }

    private static bool IsAlreadyAssigned(List<TeamSlot> teamSlots, string characterId)
    {
        return teamSlots.Any(ts =>
            ts.Characters.Any(c => c.CharacterId == characterId));
    }

    private async Task<TeamSlot?> FindMatchingTeam(
        List<TeamSlot> teamSlots,
        int bossId,
        Register register,
        Period period)
    {
        var bosses = await _bossRepository.GetAllAsync();
        var boss = bosses.ToList().FirstOrDefault(x => x.Id == bossId);
        int requireMembers = boss?.RequireMembers ?? 6;

        return teamSlots
            .Where(ts => ts.BossId == bossId)
            .Where(ts => ts.Characters.Count(c => c.CharacterId != null) < requireMembers)
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
        Period period)
    {
        var targetAvail = SlotDateCalculator.GetBestAvailability(register, period);
        var targetDateTime = SlotDateCalculator.GetNextSlotDate(targetAvail, period);

        var teamSlot = new TeamSlot
        {
            BossId = cr.BossId,
            SlotDateTime = new DateTimeOffset(targetDateTime, TimeSpan.FromHours(8)).ToOffset(TimeSpan.Zero),
            IsTemporary = false,
            IsPublished = true,
        };

        var teamSlotId = await _teamSlotRepository.CreateAsync(teamSlot);

        var firstMember = new TeamSlotCharacter { TeamSlotId = teamSlotId };
        FillSlot(firstMember, register, character, cr, player);
        await _teamSlotCharacterRepository.CreateAsync(firstMember);

        teamSlot.Id = teamSlotId;
        return teamSlot;
    }
}
