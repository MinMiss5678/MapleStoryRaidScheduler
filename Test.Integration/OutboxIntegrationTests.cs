using Application.Interface;
using Dapper;
using Infrastructure.BackgroundJobs;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// Transactional outbox 打真 Postgres：驗
///   (1) enqueue 與業務交易原子（commit 有列 / rollback 沒列）；
///   (2) dispatcher 投遞到 handler + 標 processed，再跑不重投；
///   (3) FOR UPDATE SKIP LOCKED → 多連線（多 pod）不會撈到同一列（不重投）。
/// </summary>
[Collection("pg")]
public class OutboxIntegrationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fx;
    public OutboxIntegrationTests(PostgresFixture fx) => _fx = fx;

    public Task InitializeAsync() => _fx.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<long> CountAsync(string where)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        return await conn.ExecuteScalarAsync<long>($"""SELECT count(*) FROM "OutboxMessage" {where}""");
    }

    [Fact]
    public async Task Enqueue_與交易原子_Commit有列_Rollback沒列()
    {
        // rollback → 不留列
        var rolledBack = _fx.CreateDbContext();
        await rolledBack.BeginAsync();
        await new Outbox(rolledBack).EnqueueAsync("ConfigChanged", new { a = 1 });
        await rolledBack.RollbackAsync();
        Assert.Equal(0, await CountAsync(""));

        // commit → 留一列、未處理
        var committed = _fx.CreateDbContext();
        await committed.BeginAsync();
        await new Outbox(committed).EnqueueAsync("ConfigChanged", new { a = 1 });
        await committed.CommitAsync();
        Assert.Equal(1, await CountAsync(""));
        Assert.Equal(1, await CountAsync("""WHERE "ProcessedAt" IS NULL"""));
    }

    [Fact]
    public async Task Dispatcher_投遞到handler_標processed_再跑不重投()
    {
        await EnqueueCommittedAsync("ConfigChanged");

        var handler = new RecordingHandler("ConfigChanged");
        var dispatcher = new OutboxDispatcher(_fx.ConnectionString, new[] { handler }, NullLogger<OutboxDispatcher>.Instance);

        var n1 = await dispatcher.ProcessBatchAsync(CancellationToken.None);
        Assert.Equal(1, n1);
        Assert.Equal(1, handler.Count);
        Assert.Equal(0, await CountAsync("""WHERE "ProcessedAt" IS NULL"""));   // 已標 processed

        var n2 = await dispatcher.ProcessBatchAsync(CancellationToken.None);
        Assert.Equal(0, n2);                 // 沒有待處理 → 不重投
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task Dispatcher_無對應handler_放棄不卡住()
    {
        await EnqueueCommittedAsync("UnknownType");

        var dispatcher = new OutboxDispatcher(_fx.ConnectionString,
            new[] { new RecordingHandler("ConfigChanged") }, NullLogger<OutboxDispatcher>.Instance);

        await dispatcher.ProcessBatchAsync(CancellationToken.None);

        // 無 handler → 標 processed（放棄）+ 記 LastError，不會每輪重撈卡住後續
        Assert.Equal(0, await CountAsync("""WHERE "ProcessedAt" IS NULL"""));
        Assert.Equal(1, await CountAsync("""WHERE "LastError" IS NOT NULL"""));
    }

    [Fact]
    public async Task SkipLocked_A鎖住_B撈不到同列_模擬多pod不重投()
    {
        await EnqueueCommittedAsync("ConfigChanged");

        const string claim = """
            SELECT "Id" FROM "OutboxMessage" WHERE "ProcessedAt" IS NULL
            ORDER BY "Id" FOR UPDATE SKIP LOCKED LIMIT 10
            """;

        // pod A：開交易撈住該列（持有列鎖、不 commit）
        await using var connA = new NpgsqlConnection(_fx.ConnectionString);
        await connA.OpenAsync();
        await using var txA = await connA.BeginTransactionAsync();
        var idsA = (await connA.QueryAsync<long>(claim, transaction: txA)).ToList();
        Assert.Single(idsA);

        // pod B：同時撈 → 該列被 A 鎖 → SKIP LOCKED 跳過 → 撈到 0（不會兩個 pod 同時處理）
        await using var connB = new NpgsqlConnection(_fx.ConnectionString);
        await connB.OpenAsync();
        await using var txB = await connB.BeginTransactionAsync();
        var idsB = (await connB.QueryAsync<long>(claim, transaction: txB)).ToList();
        Assert.Empty(idsB);

        await txA.RollbackAsync();
    }

    /// <summary>
    /// OutboxDispatcher.cs 註解明確寫了 at-least-once 的設計意圖：「投遞成功後才在同一交易內標
    /// processed；若『投遞完、commit 前』崩 → 重啟後該列仍未處理 → 重送（duplicate），靠 handler
    /// 冪等吸收。」但這句話從沒被實測過。這裡手動重現「handler 執行完、commit 前中斷」：
    /// 開一條交易、claim 該列、呼叫 handler、然後 rollback（模擬崩潰，不 commit）——
    /// 該列仍是未處理狀態。接著用真正的 dispatcher 重新處理（模擬重啟），驗證：
    /// 該列會被重新撈到、handler 被呼叫第二次（重送），最後才真的標成 processed。
    /// </summary>
    [Fact]
    public async Task CrashBeforeCommit_模擬崩潰未提交_重啟後重送_Handler被呼叫兩次()
    {
        await EnqueueCommittedAsync("ConfigChanged");
        var handler = new RecordingHandler("ConfigChanged");

        // 「崩潰前」的這次嘗試：claim 該列、呼叫 handler、但不 commit（模擬 commit 前中斷）
        await using (var conn = new NpgsqlConnection(_fx.ConnectionString))
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var id = await conn.QuerySingleAsync<long>(
                """SELECT "Id" FROM "OutboxMessage" WHERE "ProcessedAt" IS NULL FOR UPDATE SKIP LOCKED LIMIT 1""",
                transaction: tx);
            await handler.HandleAsync("{}", CancellationToken.None);
            await conn.ExecuteAsync("""UPDATE "OutboxMessage" SET "ProcessedAt" = now() WHERE "Id" = @id""", new { id }, tx);
            await tx.RollbackAsync(); // 模擬崩潰：commit 前中斷，標記從沒真的寫入
        }
        Assert.Equal(1, handler.Count);                                   // 崩潰前 handler 已經執行過一次（副作用已發生）
        Assert.Equal(1, await CountAsync("""WHERE "ProcessedAt" IS NULL""")); // 但 DB 上這列仍是「未處理」

        // 「重啟後」：真正的 dispatcher 重新跑一批 → 該列會被重新撈到、重送給 handler
        var dispatcher = new OutboxDispatcher(_fx.ConnectionString, new[] { handler }, NullLogger<OutboxDispatcher>.Instance);
        var processed = await dispatcher.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Equal(2, handler.Count);                                    // 重送：handler 被呼叫第二次，不是被永久卡住
        Assert.Equal(0, await CountAsync("""WHERE "ProcessedAt" IS NULL""")); // 這次真的標 processed
    }

    /// <summary>
    /// OutboxDispatcher 只標 processed，從沒有東西真的把已處理列刪掉——OutboxMessage 表會無限成長。
    /// 驗 OutboxRetentionJob：只刪「已處理 + 超過保留期」的列，未處理的列（不管多舊）跟
    /// 保留期內的已處理列都要留著（未處理列還沒投遞完，刪了就真的遺失事件）。
    /// </summary>
    [Fact]
    public async Task RetentionJob_只刪超過保留期的已處理列_留下未處理與保留期內的列()
    {
        var oldProcessedId = await InsertRowAsync(processedAt: DateTimeOffset.UtcNow.AddDays(-31));
        var recentProcessedId = await InsertRowAsync(processedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var unprocessedId = await InsertRowAsync(processedAt: null);

        var job = new OutboxRetentionJob(_fx.ConnectionString, NullLogger<OutboxRetentionJob>.Instance);
        var deleted = await job.CleanupAsync(TimeSpan.FromDays(30), CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal(0, await CountAsync($"""WHERE "Id" = {oldProcessedId}"""));      // 超過保留期 → 被刪
        Assert.Equal(1, await CountAsync($"""WHERE "Id" = {recentProcessedId}"""));   // 保留期內 → 留著
        Assert.Equal(1, await CountAsync($"""WHERE "Id" = {unprocessedId}"""));       // 未處理（即使更舊）→ 絕不能刪
    }

    private async Task<long> InsertRowAsync(DateTimeOffset? processedAt)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO "OutboxMessage"("Type","Payload","ProcessedAt")
            VALUES ('ConfigChanged', '{}', @processedAt) RETURNING "Id";
            """,
            new { processedAt });
    }

    private async Task EnqueueCommittedAsync(string type)
    {
        var ctx = _fx.CreateDbContext();
        await ctx.BeginAsync();
        await new Outbox(ctx).EnqueueAsync(type, new { });
        await ctx.CommitAsync();
    }

    private sealed class RecordingHandler : IOutboxHandler
    {
        public RecordingHandler(string type) => Type = type;
        public string Type { get; }
        public int Count { get; private set; }
        public Task HandleAsync(string payload, CancellationToken cancellationToken)
        {
            Count++;
            return Task.CompletedTask;
        }
    }
}
