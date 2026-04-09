using Application.Interface;
using Application.Queries;
using Domain.Repositories;

namespace Infrastructure.Services;

public class TeamSlotCharacterService : ITeamSlotCharacterService
{
    private readonly ITeamSlotCharacterRepository _teamSlotCharacterRepository;
    private readonly IPeriodQuery _periodQuery;

    public TeamSlotCharacterService(
        ITeamSlotCharacterRepository teamSlotCharacterRepository,
        IPeriodQuery periodQuery)
    {
        _teamSlotCharacterRepository = teamSlotCharacterRepository;
        _periodQuery = periodQuery;
    }

    public async Task DeleteByDiscordIdAndPeriodAsync(ulong discordId)
    {
        var period = await _periodQuery.GetByNowAsync();
        if (period == null) return;

        await _teamSlotCharacterRepository.DeleteByDiscordIdAndPeriodAsync(discordId, period.StartDate, period.EndDate);
    }
}