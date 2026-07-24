using System.Text.Json;
using Application.Interface;
using Domain.Entities;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Infrastructure.Services;

/// <summary>
/// Redis 版 session 快取（跨 pod 共享）。狀態存單一 Redis → 撤銷（<see cref="RemoveAsync"/>）
/// 一次 DEL 即在所有 pod 生效，修掉原 per-pod IMemoryCache「撤銷只清當下 pod」的多 pod gap。
/// fail-open 同 idempotency：Redis 不可用時 Get 當 miss（退回查 DB）、Set/Remove 記 log 後忽略。
/// </summary>
public class RedisSessionCache : ISessionCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisSessionCache> _logger;

    public RedisSessionCache(IConnectionMultiplexer redis, ILogger<RedisSessionCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private static string Key(string discordId) => $"session:{discordId}";

    public async Task<Session?> GetAsync(string discordId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(Key(discordId));
            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<Session>(value!);
        }
        catch (Exception ex)
        {
            // fail-open：Redis 不可用 → 當 cache miss，呼叫端退回查 DB（真實來源），不擋登入。
            _logger.LogWarning(ex, "Session 快取 Redis 不可用，當 miss 處理 discordId={DiscordId}", discordId);
            return null;
        }
    }

    public async Task SetAsync(string discordId, Session session, TimeSpan ttl)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(Key(discordId), JsonSerializer.Serialize(session), ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session 快取寫入失敗（Redis 不可用），忽略 discordId={DiscordId}", discordId);
        }
    }

    public async Task RemoveAsync(string discordId)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(Key(discordId));
        }
        catch (Exception ex)
        {
            // Redis 掛 → 刪不掉共享快取；但 DB 已由 SessionService 刪掉（真實來源），殘留快取有 TTL 上界。
            _logger.LogWarning(ex, "Session 快取撤銷失敗（Redis 不可用），DB 已刪、殘留有 TTL 上界 discordId={DiscordId}", discordId);
        }
    }
}
