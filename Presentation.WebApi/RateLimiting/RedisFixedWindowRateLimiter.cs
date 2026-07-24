using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace Presentation.WebApi.RateLimiting;

/// <summary>
/// Redis 版固定視窗限流器（跨 pod）。插進 .NET 內建的 <see cref="PartitionedRateLimiter"/>——
/// 每個 partition（discordId）一個實例，狀態存 Redis 故多 pod 共用同一計數。
/// 用 Lua 腳本原子做「INCR + 首次 EXPIRE」——避免「INCR 完、EXPIRE 前」那個 race。
/// Redis 不可用時 fail-open（放行 + 記 log），與 idempotency 同決策：不因限流快取抖動擋掉請求。
/// </summary>
public sealed class RedisFixedWindowRateLimiter : RateLimiter
{
    // 原子：INCR 目前視窗計數；若是本視窗第一筆（c==1）才設過期（PEXPIRE 毫秒）。回傳計數。
    private const string IncrementScript =
        "local c = redis.call('INCR', KEYS[1]) " +
        "if c == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end " +
        "return c";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger _logger;
    private readonly string _key;
    private readonly int _permitLimit;
    private readonly TimeSpan _window;
    private long _lastAccessTicks = DateTime.UtcNow.Ticks;

    public RedisFixedWindowRateLimiter(
        IConnectionMultiplexer redis, ILogger logger, string key, int permitLimit, TimeSpan window)
    {
        _redis = redis;
        _logger = logger;
        _key = key;
        _permitLimit = permitLimit;
        _window = window;
    }

    // 回報閒置時間 → PartitionedRateLimiter 才會回收沒在用的 partition（本限流器無狀態、狀態在 Redis，回收無害）。
    public override TimeSpan? IdleDuration => new TimeSpan(DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastAccessTicks));

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _lastAccessTicks, DateTime.UtcNow.Ticks);
        try
        {
            var db = _redis.GetDatabase();
            var count = (long)await db.ScriptEvaluateAsync(
                IncrementScript,
                new RedisKey[] { _key },
                new RedisValue[] { (long)_window.TotalMilliseconds });

            // 計數 <= 上限 → 放行；超過 → 擋，附 Retry-After（固定視窗最壞就是整個 window）
            return count <= _permitLimit
                ? new SimpleLease(true, null)
                : new SimpleLease(false, _window);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RateLimit Redis 不可用，fail-open 放行 key={Key}", _key);
            return new SimpleLease(true, null);
        }
    }

    // 同步路徑：ASP.NET 限流中介軟體走 async；Redis 沒有非阻塞的同步 API，這裡放行避免誤擋。
    protected override RateLimitLease AttemptAcquireCore(int permitCount) => new SimpleLease(true, null);

    protected override void Dispose(bool disposing) { }

    private sealed class SimpleLease : RateLimitLease
    {
        private readonly TimeSpan? _retryAfter;

        public SimpleLease(bool isAcquired, TimeSpan? retryAfter)
        {
            IsAcquired = isAcquired;
            _retryAfter = retryAfter;
        }

        public override bool IsAcquired { get; }

        public override IEnumerable<string> MetadataNames =>
            _retryAfter is null ? Array.Empty<string>() : new[] { MetadataName.RetryAfter.Name };

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (_retryAfter is { } ra && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = ra;
                return true;
            }
            metadata = null;
            return false;
        }
    }
}
