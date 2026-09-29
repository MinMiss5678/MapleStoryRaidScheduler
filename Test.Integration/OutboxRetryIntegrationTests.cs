using System.Runtime.CompilerServices;
using Application.Interface;
using Dapper;
using DSharpPlus.Exceptions;
using Infrastructure.BackgroundJobs;
using Infrastructure.Dapper;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// Outbox 重試：退避（NextRetryAt）、有效期限（DeliverBefore，上限建立後 24h）、錯誤分類、失效略過。
/// 案例表見 plans/2026-09-22-outbox-retry-backoff.md（C1–C10、D1）。時間比較一律用 DB now()。
/// </summary>
[Collection("pg")]
public class OutboxRetryIntegrationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fx;
    public OutboxRetryIntegrationTests(PostgresFixture fx) => _fx = fx;

    public Task InitializeAsync() => _fx.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static Exception Transient() =>
        (Exception)RuntimeHelpers.GetUninitializedObject(typeof(ServerErrorException));

    private OutboxDispatcher Dispatcher(IOutboxHandler handler) =>
        new(new NpgsqlConnectionFactory(_fx.ConnectionString), new[] { handler }, NullLogger<OutboxDispatcher>.Instance);

    /// <summary>直接插一列（可指定重試狀態與時間，時間以 DB now() 為基準的秒數偏移）。</summary>
    private async Task<long> InsertAsync(int attemptCount = 0, int? nextRetryInSec = null,
        int occurredAgoSec = 0, int? deliverBeforeFromOccurredSec = null)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        return await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO "OutboxMessage"("Type","Payload","OccurredAt","AttemptCount","NextRetryAt","DeliverBefore")
            VALUES ('TestEvent', '{}',
                    now() - (@occurredAgoSec * interval '1 second'),
                    @attemptCount,
                    CASE WHEN @nextRetryInSec IS NULL THEN NULL ELSE now() + (@nextRetryInSec * interval '1 second') END,
                    CASE WHEN @deliverSec IS NULL THEN NULL
                         ELSE now() - (@occurredAgoSec * interval '1 second') + (@deliverSec * interval '1 second') END)
            RETURNING "Id";
            """,
            new { attemptCount, nextRetryInSec, occurredAgoSec, deliverSec = deliverBeforeFromOccurredSec });
    }

    private async Task<Row> GetAsync(long id)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        return await conn.QuerySingleAsync<Row>(
            """
            SELECT "ProcessedAt" IS NOT NULL AS "Processed", "AttemptCount", "LastError",
                   EXTRACT(EPOCH FROM ("NextRetryAt" - now()))::float8 AS "RetryInSec"
            FROM "OutboxMessage" WHERE "Id" = @id
            """, new { id });
    }

    [Fact]
    public async Task C1_暫時性錯誤_未完成_次數加一_NextRetryAt落在4到6秒後()
    {
        var id = await InsertAsync();
        await Dispatcher(new FakeHandler(Transient)).ProcessBatchAsync(CancellationToken.None);

        var row = await GetAsync(id);
        Assert.False(row.Processed);
        Assert.Equal(1, row.AttemptCount);
        Assert.NotNull(row.LastError);
        Assert.NotNull(row.RetryInSec);
        Assert.InRange(row.RetryInSec!.Value, 3.5, 6.5);   // 4–6 秒，容許執行時間誤差
    }

    [Fact]
    public async Task C2_NextRetryAt未到_不會被撈()
    {
        await InsertAsync(attemptCount: 1, nextRetryInSec: 3600);
        var handler = new FakeHandler();

        var n = await Dispatcher(handler).ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(0, n);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task C3_NextRetryAt已過_被撈出並處理()
    {
        var id = await InsertAsync(attemptCount: 1, nextRetryInSec: -1);
        var handler = new FakeHandler();

        await Dispatcher(handler).ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(1, handler.Count);
        Assert.True((await GetAsync(id)).Processed);
    }

    [Fact]
    public async Task C4_暫時性錯誤_次數已超過5_未過期仍繼續重試()
    {
        var id = await InsertAsync(attemptCount: 7);
        await Dispatcher(new FakeHandler(Transient)).ProcessBatchAsync(CancellationToken.None);

        var row = await GetAsync(id);
        Assert.False(row.Processed);
        Assert.Equal(8, row.AttemptCount);
        Assert.NotNull(row.RetryInSec);
    }

    [Fact]
    public async Task C5_無法分類錯誤_前4次退避重試_第5次放棄()
    {
        var retrying = await InsertAsync(attemptCount: 3);   // 這次是第 4 次
        var givingUp = await InsertAsync(attemptCount: 4);   // 這次是第 5 次
        await Dispatcher(new FakeHandler(() => new InvalidOperationException("bug"))).ProcessBatchAsync(CancellationToken.None);

        var r1 = await GetAsync(retrying);
        Assert.False(r1.Processed);
        Assert.Equal(4, r1.AttemptCount);
        Assert.NotNull(r1.RetryInSec);

        var r2 = await GetAsync(givingUp);
        Assert.True(r2.Processed);
        Assert.Equal(5, r2.AttemptCount);
        Assert.NotNull(r2.LastError);
    }

    [Fact]
    public async Task C6_DeliverBefore已過_不送出_標完成_LastError為expired()
    {
        var id = await InsertAsync(occurredAgoSec: 120, deliverBeforeFromOccurredSec: 60);   // 期限在 1 分鐘前
        var handler = new FakeHandler();

        await Dispatcher(handler).ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(0, handler.Count);
        var row = await GetAsync(id);
        Assert.True(row.Processed);
        Assert.Equal("expired", row.LastError);
    }

    [Fact]
    public async Task C7_DeliverBefore為NULL_建立25小時_預設24小時已過期()
    {
        var id = await InsertAsync(occurredAgoSec: 25 * 3600);
        var handler = new FakeHandler();

        await Dispatcher(handler).ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(0, handler.Count);
        Assert.Equal("expired", (await GetAsync(id)).LastError);
    }

    [Fact]
    public async Task C8_DeliverBefore設48小時_建立25小時_24小時上限優先已過期()
    {
        var id = await InsertAsync(occurredAgoSec: 25 * 3600, deliverBeforeFromOccurredSec: 48 * 3600);
        var handler = new FakeHandler();

        await Dispatcher(handler).ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(0, handler.Count);
        Assert.Equal("expired", (await GetAsync(id)).LastError);
    }

    [Fact]
    public async Task C9_DeliverBefore未到_正常送出()
    {
        var id = await InsertAsync(deliverBeforeFromOccurredSec: 3600);
        var handler = new FakeHandler();

        await Dispatcher(handler).ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(1, handler.Count);
        var row = await GetAsync(id);
        Assert.True(row.Processed);
        Assert.Null(row.LastError);
    }

    [Fact]
    public async Task C10_handler判定失效_標完成_LastError記原因_不再重試()
    {
        var id = await InsertAsync();
        var handler = new FakeHandler(() => new OutboxDeliverySkippedException("invite no longer pending"));

        await Dispatcher(handler).ProcessBatchAsync(CancellationToken.None);
        await Dispatcher(handler).ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(1, handler.Count);   // 第二輪不再撈
        var row = await GetAsync(id);
        Assert.True(row.Processed);
        Assert.Equal("skipped: invite no longer pending", row.LastError);
    }

    [Fact]
    public async Task D1_Enqueue帶deliverBefore_寫入欄位_不帶則為NULL()
    {
        var deadline = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var ctx = _fx.CreateDbContext();
        await ctx.BeginAsync();
        await new Outbox(ctx).EnqueueAsync("TestEvent", new { }, deadline);
        await new Outbox(ctx).EnqueueAsync("TestEvent", new { });
        await ctx.CommitAsync();

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        // Npgsql 把 timestamptz 讀成 UTC DateTime（Dapper 不直接轉 DateTimeOffset）→ 以 UTC 比對
        var values = (await conn.QueryAsync<DateTime?>(
            """SELECT "DeliverBefore" FROM "OutboxMessage" ORDER BY "Id" """)).ToList();
        Assert.Equal(deadline.UtcDateTime, values[0]);
        Assert.Null(values[1]);
    }

    private sealed class Row
    {
        public bool Processed { get; init; }
        public int AttemptCount { get; init; }
        public string? LastError { get; init; }
        public double? RetryInSec { get; init; }
    }

    private sealed class FakeHandler : IOutboxHandler
    {
        private readonly Func<Exception>? _fail;
        public FakeHandler(Func<Exception>? fail = null) => _fail = fail;
        public string Type => "TestEvent";
        public int Count { get; private set; }
        public Task HandleAsync(string payload, CancellationToken cancellationToken)
        {
            Count++;
            if (_fail != null) throw _fail();
            return Task.CompletedTask;
        }
    }
}
