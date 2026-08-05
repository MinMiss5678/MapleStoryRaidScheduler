using Domain.Entities;

namespace Domain.Repositories;

public interface ICharacterBossClearRepository
{
    Task CreateAsync(CharacterBossClear clear);
    Task<IEnumerable<CharacterBossClear>> GetByCharacterIdAsync(string characterId);
    Task<int> DeleteByCharacterIdAsync(string characterId);
}
