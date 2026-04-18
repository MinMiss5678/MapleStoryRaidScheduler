using Application.DTOs;

namespace Application.Interface;

public interface IRegisterService
{
    Task CreateAsync(RegisterCreateCommand command);
    Task UpdateAsync(RegisterUpdateCommand command);
    Task DeleteAsync(ulong discordId, int id);
}
