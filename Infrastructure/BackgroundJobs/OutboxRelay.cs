using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Outbox → Message Queue 的 <b>relay</b>（取代原本「dispatcher 直接呼叫 handler」）。
/// 輪詢已提交的 outbox 列 → <c>XADD</c> 發布到 Redis Stream → 標 <c>ProcessedAt</c>（發布成功才標）。
/// 實際處理由 <see cref="OutboxStreamConsumer"/> 從 stream 消費——生產/消費解耦。
///
/// 設計：
/// - <b>FOR UPDATE SKIP LOCKED</b>：多 relay（多 pod）各撈不相交批、不重複發布。
/// - <b>at-least-once 發布</b>：XADD 成功、標 processed 前崩 → 重啟重發（stream 重複）→ 靠 consumer 冪等吸收。
/// - <b>專屬連線</b>：自開 <see cref="NpgsqlConnection"/>，不共用 app 的 singleton 連線。
/// </summary>
public class OutboxRelay : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private const string ClaimSql =
        """
        SELECT "Id", "Type", "Payload"
        FROM "OutboxMessage"
        WHERE "ProcessedAt" IS NULL
        ORDER BY "Id"
        FOR UPDATE SKIP LOCKED
        LIMIT @Limit
        """;
    private const string MarkProcessedSql =
        """UPDATE "OutboxMessage" SET "ProcessedAt" = now() WHERE "Id" = @Id""";

    private readonly string _connectionString;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<OutboxRelay> _logger;
    private readonly string _streamKey;

    public OutboxRelay(string connectionString, IConnectionMultiplexer redis, ILogger<OutboxRelay> logger, string? streamKey = null)
    {
        _connectionString = connectionString;
        _redis = redis;
        _logger = logger;
        _streamKey = streamKey ?? OutboxStream.Key;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxRelay is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await RelayBatchAsync(stoppingToken);
                if (published >= BatchSize)
                    continue; // 撈滿 → 可能還有，立即再撈
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxRelay batch failed");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal async Task<int> RelayBatchAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        // 交易包住整批：FOR UPDATE 鎖持有到 commit → 其他 relay SKIP 掉這些列
        await using var tx = await conn.BeginTransactionAsync(ct);

        var rows = (await conn.QueryAsync<OutboxRow>(ClaimSql, new { Limit = BatchSize }, tx)).ToList();
        var db = _redis.GetDatabase();

        foreach (var row in rows)
        {
            // 發布到 Redis Stream（自描述：type + payload + 來源 outboxId 便於追）
            await db.StreamAddAsync(_streamKey, new NameValueEntry[]
            {
                new("type", row.Type),
                new("payload", row.Payload),
                new("outboxId", row.Id)
            });
            await conn.ExecuteAsync(MarkProcessedSql, new { row.Id }, tx);
        }

        await tx.CommitAsync(ct);
        return rows.Count;
    }

    private sealed class OutboxRow
    {
        public long Id { get; init; }
        public string Type { get; init; } = "";
        public string Payload { get; init; } = "";
    }
}
