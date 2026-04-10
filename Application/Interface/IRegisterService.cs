using Application.DTOs;
using Domain.Entities;

namespace Application.Interface;

public interface IRegisterService
{
    Task CreateAsync(Register register);
    Task UpdateAsync(RegisterUpdateCommand command);
    Task DeleteAsync(ulong discordId, int id);
}
