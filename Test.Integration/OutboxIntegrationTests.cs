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
