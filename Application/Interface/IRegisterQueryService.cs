using Application.DTOs;
using Domain.Entities;

namespace Application.Interface;

public interface IRegisterQueryService
{
    Task<RegisterDto> GetAsync(ulong discordId);
    Task<RegisterDto> GetLastAsync(ulong discordId);
    Task<IEnumerable<TeamSlotCharacter>> GetByQueryAsync(RegisterGetByQueryRequest request);
}
