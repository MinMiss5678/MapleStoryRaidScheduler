using Application.DTOs;
using Domain.Entities;

namespace Application.Queries;

public interface ITeamSlotQuery
{
    Task<IEnumerable<TeamSlotCharacterDto>> GetByPeriodAndBossIdAsync(Period period, int bossId);
    Task<IEnumerable<TeamSlotCharacterDto>> GetByPeriodAndDiscordIdAsync(Period period, ulong discordId);
    Task<IEnumerable<TeamSlotCharacterDto>> GetBySlotDateTimeAsync(DateTimeOffset slotDateTime);
}