namespace Domain.Repositories;

public interface ICharacterPreferredBossRepository
{
    /// <summary>某角色目前偏好的王 Id 集合。</summary>
    Task<IEnumerable<int>> GetBossIdsByCharacterAsync(string characterId);

    /// <summary>整批取代：刪掉該角色現有偏好、插入新集合（同 UoW 交易 → 原子）。空集合＝清空偏好。</summary>
    Task ReplaceAsync(string characterId, IEnumerable<int> bossIds);
}
