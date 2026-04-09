using Application.DTOs;
using Domain.Entities;

namespace Application.Interface;

public interface IRegisterService
{
    Task<RegisterDto?> GetAsync(ulong discordId);
    Task<RegisterDto?> GetLastAsync(ulong discordId);
    Task<IEnumerable<TeamSlotCharacter>> GetByQueryAsync(RegisterGetByQueryRequest request);
    Task CreateAsync(Register register);
    Task UpdateAsync(Register character);
    Task DeleteAsync(ulong discordId, int id);
}