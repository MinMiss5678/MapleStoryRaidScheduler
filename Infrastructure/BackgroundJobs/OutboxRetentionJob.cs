using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Outbox 保留列清理：已處理（ProcessedAt 非 null）且超過保留期限的列直接刪除。
/// OutboxDispatcher 只讀「待處理」列（partial index），已處理列堆積不影響它的效能，
/// 但長期不清仍會讓 OutboxMessage 表無限成長（磁碟、pg_dump/還原時間、稽核查詢變慢）——這支job補上。
/// 跟 OutboxDispatcher 一樣自開專屬連線，不共用 app 的 DbContext/連線。
/// </summary>
public class OutboxRetentionJob : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(24);

    private const string DeleteSql =
        """DELETE FROM "OutboxMessage" WHERE "ProcessedAt" IS NOT NULL AND "ProcessedAt" < @Threshold""";

    private readonly string _connectionString;
    private readonly ILogger<OutboxRetentionJob> _logger;

    public OutboxRetentionJob(string connectionString, ILogger<OutboxRetentionJob> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxRetentionJob is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = await CleanupAsync(Retention, stoppingToken);
                if (deleted > 0)
                    _logger.LogInformation("OutboxRetentionJob 清除 {Count} 筆已處理超過 {Days} 天的列", deleted, Retention.TotalDays);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxRetentionJob cleanup failed");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    // internal：供整合測確定性地跑一次（不靠 24 小時輪詢）
    internal async Task<int> CleanupAsync(TimeSpan retention, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var threshold = DateTimeOffset.UtcNow - retention;
        return await conn.ExecuteAsync(DeleteSql, new { Threshold = threshold });
    }
}
