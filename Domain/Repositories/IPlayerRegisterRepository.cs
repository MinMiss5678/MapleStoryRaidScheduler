using Domain.Entities;

namespace Domain.Repositories;

public interface IPlayerRegisterRepository
{
    Task<IEnumerable<PlayerCharacterRegister>> GetListAsync(ulong discordId, int periodId);
    Task<bool> ExistAsync(ulong discordId, int periodId);
    Task<int?> GetIdAsync(ulong discordId, int periodId);
    Task<int> CreateAsync(Register register);
    Task<int> UpdateAsync(Register register);
    Task DeleteAsync(ulong discordId, int id);
}
