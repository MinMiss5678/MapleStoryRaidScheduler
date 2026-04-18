using Application.DTOs;

namespace Application.Interface;

public interface IRegisterQueryService
{
    Task<RegisterDto> GetAsync(ulong discordId);
    Task<RegisterDto> GetLastAsync(ulong discordId);
    Task<IEnumerable<TeamSlotMemberDto>> GetByQueryAsync(RegisterGetByQueryRequest request);
}
