using Domain.Entities;

namespace Domain.Repositories;

public interface ICharacterRegisterRepository
{
    Task CreateAsync(CharacterRegister characterRegister);
    Task UpdateAsync(CharacterRegister characterRegister);
    Task<bool> DeleteAsync(int id, int playerRegisterId);
    Task<int> DeleteByPlayerRegisterIdAsync(int playerRegisterId);
    Task<int> DeleteByCharacterIdAsync(string characterId);
}
