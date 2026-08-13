using Application.DTOs;

namespace Application.Queries;

public interface ITeamSlotQuery
{
    Task<IEnumerable<TeamSlotCharacterDto>> GetBySlotDateTimeAsync(DateTimeOffset slotDateTime);
}
