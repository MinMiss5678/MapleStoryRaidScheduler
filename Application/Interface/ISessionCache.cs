using Domain.Entities;

namespace Application.Interface;

/// <summary>
/// Session 快取抽象。實作以 Redis 提供跨 pod 的共享狀態（取代原本的 per-pod IMemoryCache）——
/// 關鍵在 <see cref="RemoveAsync"/>：撤銷一次刪除共享項即在所有 pod 立即生效，
/// 不再是「只清當下 pod、其他 pod 等 TTL」的多 pod gap。
/// 儲存不可用時採 <b>fail-open</b>：Get 當 miss（呼叫端退回查 DB，DB 才是真實來源），
/// Set/Remove 記 log 後忽略（DB 已由 SessionService 兜底，殘留項有 TTL 上界）。
/// </summary>
public interface ISessionCache
{
    /// <summary>讀快取；miss 或 Redis 不可用時回 <c>null</c>（呼叫端退回查 DB）。以 discordId 為 key。</summary>
    Task<Session?> GetAsync(string discordId);

    /// <summary>寫快取，TTL 對齊 AccessToken 過期。</summary>
    Task SetAsync(string discordId, Session session, TimeSpan ttl);

    /// <summary>撤銷：刪除共享快取項 → 所有 pod 立即失效（登出／強制下線用）。</summary>
    Task RemoveAsync(string discordId);
}
