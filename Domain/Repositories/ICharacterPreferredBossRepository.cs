namespace Domain.Repositories;

/// <summary>
/// 角色的偏好王（複選，CharacterPreferredBoss 純多對多表）：候選匹配的**軟訊號**來源。
/// 用於隊長挑候選時「偏好本王」排前 + 標記，**不做硬篩**（守 boss-agnostic，見計畫 2026-08-24-preferred-boss-candidate-signal）。
/// </summary>
public interface ICharacterPreferredBossRepository
{
    /// <summary>某角色目前偏好的王 Id 集合。</summary>
    Task<IEnumerable<int>> GetBossIdsByCharacterAsync(string characterId);

    /// <summary>整批取代：刪掉該角色現有偏好、插入新集合（同 UoW 交易 → 原子）。空集合＝清空偏好。</summary>
    Task ReplaceAsync(string characterId, IEnumerable<int> bossIds);
}
