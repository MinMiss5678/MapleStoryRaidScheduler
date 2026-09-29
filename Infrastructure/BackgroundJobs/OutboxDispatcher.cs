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
    private const int MaxAttempts = 5;                              // 只管「無法分類」的錯誤（程式錯誤）：超過 → 放棄、記 LastError
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private const string ClaimSql =
        """
        SELECT "Id", "Type", "Payload", "AttemptCount",
               -- 有效期限（DB 時鐘）：DeliverBefore 未設＝建立後 24h；一律不超過建立後 24h
               LEAST(COALESCE("DeliverBefore", "OccurredAt" + interval '24 hours'),
                     "OccurredAt" + interval '24 hours') <= now() AS "Expired"
        FROM "OutboxMessage"
        WHERE "ProcessedAt" IS NULL
          AND ("NextRetryAt" IS NULL OR "NextRetryAt" <= now())   -- 退避中的列到期前不撈
        ORDER BY "Id"
        FOR UPDATE SKIP LOCKED
        LIMIT @Limit
        """;
    private const string MarkProcessedSql =
        """UPDATE "OutboxMessage" SET "ProcessedAt" = now() WHERE "Id" = @Id""";
    // 下次可派發時間以 DB now() 為基準（多 pod 免 app 時鐘偏移）；delay 由 OutboxRetryPolicy 算好傳入
    private const string MarkFailedSql =
        """
        UPDATE "OutboxMessage"
        SET "AttemptCount" = "AttemptCount" + 1, "LastError" = @Error,
            "NextRetryAt" = now() + (@DelaySeconds * interval '1 second')
        WHERE "Id" = @Id
        """;
    private const string GiveUpSql =
        """UPDATE "OutboxMessage" SET "ProcessedAt" = now(), "AttemptCount" = "AttemptCount" + 1, "LastError" = @Error WHERE "Id" = @Id""";
    // 過期／失效：沒有真的嘗試投遞 → 不加次數，只結案並記原因
    private const string CloseSql =
        """UPDATE "OutboxMessage" SET "ProcessedAt" = now(), "LastError" = @Error WHERE "Id" = @Id""";

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

            if (row.Expired)
            {
                // 過了有效期限（例：Discord 長時間故障）→ 現在送已沒意義，放棄；Error 會觸發 Seq 警示信
                _logger.LogError("Outbox 超過有效期限仍未送達，放棄 Id={Id} Type={Type} Attempts={Attempts}", row.Id, row.Type, row.AttemptCount);
                await conn.ExecuteAsync(CloseSql, new { row.Id, Error = "expired" }, tx);
                continue;
            }

            try
            {
                await handler.HandleAsync(row.Payload, ct);
                await conn.ExecuteAsync(MarkProcessedSql, new { row.Id }, tx);
            }
            catch (OutboxDeliverySkippedException skipped)
            {
                // handler 判定已失效（邀請已撤、隊已滿…）→ 正常業務結案，不重試、不警示
                _logger.LogInformation("Outbox 通知已失效，略過 Id={Id} Type={Type} Reason={Reason}", row.Id, row.Type, skipped.Message);
                await conn.ExecuteAsync(CloseSql, new { row.Id, Error = $"skipped: {skipped.Message}" }, tx);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                var attempt = row.AttemptCount + 1;
                // 暫時性（Discord 5xx / 429 / 網路 / 逾時）→ 不計次數，退避到有效期限；其餘（程式錯誤）→ 次數上限後放棄
                if (!OutboxRetryPolicy.IsTransient(ex) && attempt >= MaxAttempts)
                {
                    _logger.LogError(ex, "Outbox 投遞達重試上限，放棄 Id={Id} Type={Type}", row.Id, row.Type);
                    await conn.ExecuteAsync(GiveUpSql, new { row.Id, Error = ex.Message }, tx);
                }
                else
                {
                    var delay = OutboxRetryPolicy.ComputeDelaySeconds(attempt, Random.Shared.NextDouble());
                    _logger.LogWarning(ex, "Outbox 投遞失敗，{Delay}s 後重試 Id={Id} Type={Type} Attempt={Attempt}", delay, row.Id, row.Type, attempt);
                    await conn.ExecuteAsync(MarkFailedSql, new { row.Id, Error = ex.Message, DelaySeconds = delay }, tx);
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
        public bool Expired { get; init; }
    }
}
