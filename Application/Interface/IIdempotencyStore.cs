namespace Application.Interface;

/// <summary>
/// 重複提交防護的「去重儲存」抽象。實作以 Redis 提供跨 pod 的共享狀態
/// （取代原本的 per-pod IMemoryCache）。middleware 只依賴此介面，不綁 Redis。
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// 標記一個 idempotency key：
    /// - 第一次看到 → 回 <c>true</c>（放行）。
    /// - <paramref name="ttl"/> 內已看過 → 回 <c>false</c>（重複，呼叫端回 409）。
    /// 儲存不可用時採 <b>fail-open</b>：回 <c>true</c>（放行）+ 記 log，
    /// 不因快取層抖動擋掉所有寫入（真正的重複由 DB 約束 / advisory lock 兜底）。
    /// </summary>
    Task<bool> TryMarkAsync(string key, TimeSpan ttl);
}
