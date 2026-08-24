using Application.Interface;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Outbox 派發器：輪詢已提交的 outbox 列 → 依 Type 派給 <see cref="IOutboxHandler"/> → 標記 processed。
///
/// 設計重點：
/// - <b>FOR UPDATE SKIP LOCKED</b>：多個 dispatcher（多 pod）併跑時各撈不相交的批、互不重送、免選 leader。
/// - <b>at-least-once</b>：投遞成功後才在同一交易內標 processed；若「投遞完、commit 前」崩 →
///   重啟後該列仍未處理 → 重送（duplicate），靠 handler 冪等吸收。
/// - <b>專屬連線</b>：自己開 <see cref="NpgsqlConnection"/>，不共用 app 的 DbContext/連線
///   （bot 的連線是 singleton，共用會與 Discord 事件/計時器互踩）。
/// </summary>
public class OutboxDispatcher : BackgroundService
{
    private const int BatchSize = 20;
    private const int MaxAttempts = 5;                              // 超過視為毒訊息 → 放棄、記 LastError
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private const string ClaimSql =
        """
        SELECT "Id", "Type", "Payload", "AttemptCount"
        FROM "OutboxMessage"
        WHERE "ProcessedAt" IS NULL
        ORDER BY "Id"
        FOR UPDATE SKIP LOCKED
        LIMIT @Limit
        """;
    private const string MarkProcessedSql =
        """UPDATE "OutboxMessage" SET "ProcessedAt" = now() WHERE "Id" = @Id""";
    private const string MarkFailedSql =
        """UPDATE "OutboxMessage" SET "AttemptCount" = "AttemptCount" + 1, "LastError" = @Error WHERE "Id" = @Id""";
    private const string GiveUpSql =
        """UPDATE "OutboxMessage" SET "ProcessedAt" = now(), "AttemptCount" = "AttemptCount" + 1, "LastError" = @Error WHERE "Id" = @Id""";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IReadOnlyDictionary<string, IOutboxHandler> _handlers;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(IDbConnectionFactory connectionFactory, IEnumerable<IOutboxHandler> handlers, ILogger<OutboxDispatcher> logger)
    {
        _connectionFactory = connectionFactory;
        _handlers = handlers.ToDictionary(h => h.Type);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxDispatcher is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessBatchAsync(stoppingToken);
                // 撈滿一批 → 可能還有，立即再撈；否則睡一下再輪詢
                if (processed >= BatchSize)
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxDispatcher batch failed");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    // internal：供整合測確定性地跑一批（不靠計時輪詢）
    internal async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        // 交易包住整批：FOR UPDATE 的鎖持有到 commit → 其他 dispatcher SKIP 掉這些列
        await using var tx = await conn.BeginTransactionAsync(ct);

        var rows = (await conn.QueryAsync<OutboxRow>(ClaimSql, new { Limit = BatchSize }, tx)).ToList();

        foreach (var row in rows)
        {
            if (!_handlers.TryGetValue(row.Type, out var handler))
            {
                // 沒有對應 handler → 永遠處理不了，直接放棄避免卡住後續（記錯誤便於察覺漏註冊）
                _logger.LogWarning("Outbox 無對應 handler，放棄 Id={Id} Type={Type}", row.Id, row.Type);
                await conn.ExecuteAsync(GiveUpSql, new { row.Id, Error = $"no handler for type '{row.Type}'" }, tx);
                continue;
            }

            try
            {
                await handler.HandleAsync(row.Payload, ct);
                await conn.ExecuteAsync(MarkProcessedSql, new { row.Id }, tx);
            }
            catch (Exception ex)
            {
                if (row.AttemptCount + 1 >= MaxAttempts)
                {
                    _logger.LogError(ex, "Outbox 投遞達重試上限，放棄 Id={Id} Type={Type}", row.Id, row.Type);
                    await conn.ExecuteAsync(GiveUpSql, new { row.Id, Error = ex.Message }, tx);
                }
                else
                {
                    _logger.LogWarning(ex, "Outbox 投遞失敗，稍後重試 Id={Id} Type={Type}", row.Id, row.Type);
                    await conn.ExecuteAsync(MarkFailedSql, new { row.Id, Error = ex.Message }, tx);
                }
            }
        }

        await tx.CommitAsync(ct);
        return rows.Count;
    }

    private sealed class OutboxRow
    {
        public long Id { get; init; }
        public string Type { get; init; } = "";
        public string Payload { get; init; } = "";
        public int AttemptCount { get; init; }
    }
}
