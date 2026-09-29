using DSharpPlus.Exceptions;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Outbox 重試策略（純函式，可單元測；見 plans/2026-09-22-outbox-retry-backoff.md）。
/// </summary>
internal static class OutboxRetryPolicy
{
    private const int BaseSeconds = 5;
    private const int CapSeconds = 600;      // 退避封頂 10 分鐘：Discord 長時間故障時仍每 ~10 分鐘探一次
    private const double JitterMin = 0.8;    // ±20%：同批多筆 / 多 pod 同時失敗時錯開，避免同步猛打 Discord
    private const double JitterSpan = 0.4;

    /// <summary>
    /// 第 <paramref name="attempt"/> 次重試前的等待秒數：min(5 × 2^(n-1), 600) × jitter[0.8, 1.2)。
    /// <paramref name="jitterSample"/> ∈ [0, 1)（正式用 Random.Shared.NextDouble()，測試可固定）。
    /// </summary>
    public static int ComputeDelaySeconds(int attempt, double jitterSample)
    {
        // 指數在超過封頂後就沒意義，先夾住次方避免 2^n 溢位
        var exponent = Math.Clamp(attempt - 1, 0, 30);
        var raw = Math.Min(BaseSeconds * Math.Pow(2, exponent), CapSeconds);
        var jittered = raw * (JitterMin + JitterSpan * jitterSample);
        return Math.Max(1, (int)Math.Round(jittered));
    }

    /// <summary>
    /// 暫時性錯誤（Discord 5xx、429 內部重試用完、網路、逾時）→ 退避到有效期限為止，不計次數上限。
    /// 其餘（程式錯誤等）→ 維持次數上限，盡快放棄觸發警示。停機造成的取消由呼叫端先行處理。
    /// </summary>
    public static bool IsTransient(Exception ex) => ex is ServerErrorException
        or RateLimitException
        or HttpRequestException
        or TimeoutException
        or TaskCanceledException
        or IOException;
}
