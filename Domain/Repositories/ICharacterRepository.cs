using Domain.Entities;

namespace Domain.Repositories;

public interface ICharacterRepository
{
    Task<int> CreateAsync(Character character);
    Task<int> UpdateAsync(Character character);
    Task<int> DeleteAsync(ulong discordId, string id);

    /// <summary>
    /// 設定某玩家角色的參戰 opt-in（period-less §8 Phase 2）：listed 的角色設 true、該玩家其餘角色設 false
    /// （replace 語意，對齊一次報名/編輯的 opt-in 狀態）。
    /// </summary>
    Task SetSeekingRaidForDiscordAsync(ulong discordId, IReadOnlyCollection<string> seekingCharacterIds);
}
