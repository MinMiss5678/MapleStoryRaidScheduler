using Application.Interface;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// 即時找隊意圖（LfgIntent）TTL 清理（period-less §8 Phase 4b）：刪掉已過期（ExpiresAt &lt;= now）的意圖。
/// 看板/候選讀取本就過濾 ExpiresAt &gt; now（過期不顯示），此 job 只是回收空間避免無限成長。
/// 跟 OutboxRetentionJob 一樣自開專屬連線、不共用 app DbContext。
/// 過期的隊（instant/scheduled）刻意不刪——保留成員歷史與退團率信號，且已被時間窗查詢濾掉。
/// </summary>
public class LfgIntentCleanupJob : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    private const string DeleteSql = """DELETE FROM "LfgIntent" WHERE "ExpiresAt" <= @Now""";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<LfgIntentCleanupJob> _logger;

    public LfgIntentCleanupJob(IDbConnectionFactory connectionFactory, ILogger<LfgIntentCleanupJob> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LfgIntentCleanupJob is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = await CleanupAsync(stoppingToken);
                if (deleted > 0)
                    _logger.LogInformation("LfgIntentCleanupJob 清除 {Count} 筆過期找隊意圖", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LfgIntentCleanupJob cleanup failed");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    // internal：供整合測確定性地跑一次（不靠 1 小時輪詢）
    internal async Task<int> CleanupAsync(CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteAsync(DeleteSql, new { Now = DateTimeOffset.UtcNow });
    }
}
