using Domain.Entities;

namespace Domain.Repositories;

public interface ICharacterBossClearRepository
{
    Task CreateAsync(CharacterBossClear clear);

    /// <summary>同角色同王一筆（uq_charbossclear）→ upsert：不存在則插入、存在則覆寫 ClearCount。</summary>
    Task UpsertAsync(CharacterBossClear clear);

    Task<IEnumerable<CharacterBossClear>> GetByCharacterIdAsync(string characterId);
    Task<int> DeleteByCharacterIdAsync(string characterId);
}
