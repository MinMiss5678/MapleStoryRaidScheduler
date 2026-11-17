using Application.Interface;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Infrastructure.Services;

/// <summary>
/// Redis 版的去重儲存。用 <c>SET key "1" NX EX ttl</c>（單一原子命令）達成
/// 「第一次成功寫入、之後回 false」——跨 pod 共享，且比「先讀再寫」少一個 race。
/// </summary>
public class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisIdempotencyStore> _logger;

    public RedisIdempotencyStore(IConnectionMultiplexer redis, ILogger<RedisIdempotencyStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<bool> TryMarkAsync(string key, TimeSpan ttl)
    {
        try
        {
            var db = _redis.GetDatabase();
            // When.NotExists = SET NX：有設到（第一次）回 true；已存在（重複）回 false。原子、無 race。
            return await db.StringSetAsync($"idempotency:{key}", "1", ttl, When.NotExists);
        }
        catch (Exception ex)
        {
            // fail-open：Redis 不可用 → 放行（當成第一次），記 log。
            // 不讓去重快取的抖動擋掉寫入；真正重複由報名 ExistAsync + auto-assign advisory lock 兜底。
            _logger.LogWarning(ex, "Idempotency Redis 不可用，fail-open 放行 key={Key}", key);
            return true;
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync($"idempotency:{key}");
        }
        catch (Exception ex)
        {
            // fail-open：撤銷失敗最壞只是 ttl 內用同一把 key 重送會被 409 擋，不值得讓請求本身失敗。
            _logger.LogWarning(ex, "Idempotency Redis 不可用，撤銷標記失敗 key={Key}", key);
        }
    }
}
