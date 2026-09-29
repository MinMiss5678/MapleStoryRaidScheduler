using System.Runtime.CompilerServices;
using System.Text.Json;
using DSharpPlus.Exceptions;
using Infrastructure.BackgroundJobs;
using Xunit;

namespace Test;

/// <summary>
/// Outbox 重試策略（plans/2026-09-22-outbox-retry-backoff.md）：
/// 退避 = min(5 × 2^(n-1), 600) 秒 × jitter[0.8, 1.2)；錯誤分類決定「退避到期限」或「5 次上限」。
/// </summary>
public class OutboxRetryPolicyTests
{
    // jitterSample ∈ [0,1) 映射到倍率 [0.8, 1.2)：取 0 得下界、取接近 1 得上界，驗證範圍而不靠隨機
    [Theory]
    [InlineData(1, 4, 6)]       // A1：基準 5
    [InlineData(2, 8, 12)]      // A2：基準 10
    [InlineData(3, 16, 24)]     // A3：基準 20
    [InlineData(8, 480, 720)]   // A4：基準 640 → 封頂 600
    [InlineData(50, 480, 720)]  // A5：極大值不溢位，仍封頂
    public void ComputeDelaySeconds_指數退避_封頂_jitter在範圍內(int attempt, int min, int max)
    {
        var low = OutboxRetryPolicy.ComputeDelaySeconds(attempt, jitterSample: 0.0);
        var high = OutboxRetryPolicy.ComputeDelaySeconds(attempt, jitterSample: 0.999999);

        Assert.InRange(low, min, max);
        Assert.InRange(high, min, max);
        Assert.True(low < high);   // jitter 確實有作用
    }

    public static TheoryData<Exception> TransientExceptions => new()
    {
        (Exception)RuntimeHelpers.GetUninitializedObject(typeof(ServerErrorException)),   // B1：Discord 5xx
        (Exception)RuntimeHelpers.GetUninitializedObject(typeof(RateLimitException)),     // B2：429（DSharpPlus 內部重試用完）
        new HttpRequestException("network"),                                              // B3
        new TimeoutException(),                                                           // B4
        new TaskCanceledException(),                                                      // B5：HTTP 逾時（非停機）
    };

    [Theory]
    [MemberData(nameof(TransientExceptions))]
    public void IsTransient_Discord與網路暫時性錯誤_為暫時性(Exception ex)
    {
        Assert.True(OutboxRetryPolicy.IsTransient(ex));
    }

    public static TheoryData<Exception> NonTransientExceptions => new()
    {
        new InvalidOperationException("payload 解析失敗"),   // B6：程式錯誤
        new JsonException("bad json"),                     // B7
    };

    [Theory]
    [MemberData(nameof(NonTransientExceptions))]
    public void IsTransient_程式錯誤_為無法分類(Exception ex)
    {
        Assert.False(OutboxRetryPolicy.IsTransient(ex));
    }
}
