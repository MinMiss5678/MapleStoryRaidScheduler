using Domain.Entities;

namespace Domain.Repositories;

public interface IPlayerRepository
{
    Task<int> CreateAsync(Player player);
    Task<bool> ExistAsync(ulong discordId);
    Task<Player?> GetAsync(ulong discordId);
    Task<int> UpdateRoleAsync(ulong discordId, string role);

    /// <summary>
    /// 心跳：玩家做了組隊實質動作後更新其最後活躍時戳（常設時段新鮮度衰退，見
    /// plans/2026-09-01-availability-freshness-decay.md）。**節流**：僅當 LastAffirmedAt 為 NULL
    /// 或已舊於 1 天才真寫，避免同玩家同日多次動作寫放大（mirror Session sliding 節流）。
    /// </summary>
    Task BumpLastAffirmedAsync(ulong discordId);

    /// <summary>
    /// 階段二 nudge 對象：**參戰中**、`LastAffirmedAt` 已舊於 <paramref name="nudgeAfterDays"/>（門檻 − 前置天），
    /// 且「上次提醒後又有活動」（`FreshnessNudgedAt IS NULL OR <= LastAffirmedAt`）的玩家 DiscordId。
    /// `LastAffirmedAt` 為 NULL（永久新鮮）者不列入。見 plans/2026-09-01-availability-freshness-decay.md。
    /// </summary>
    Task<IReadOnlyCollection<ulong>> GetFreshnessNudgeTargetsAsync(int nudgeAfterDays);

    /// <summary>標記已對該玩家發過新鮮度提醒（避免下輪重送）。</summary>
    Task MarkFreshnessNudgedAsync(ulong discordId);
}
