using Application.Interface;
using Dapper;
using Infrastructure.BackgroundJobs;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace Test.Integration;

/// <summary>
/// Outbox → Redis Streams（MQ plan Phase 1）打真 Postgres + Redis：
///   (1) enqueue 與業務交易原子；(2) relay 發布到 stream + 標 processed；
///   (3) FOR UPDATE SKIP LOCKED 多 relay 不重發；(4) consumer 消費 + ACK；
///   (5) 處理失敗留 PEL → XAUTOCLAIM 由另一 consumer 重投（at-least-once）；(6) 無 handler → ACK 丟棄。
/// </summary>
[Collection("pg")]
public class OutboxIntegrationTests : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redisFx;

    public OutboxIntegrationTests(PostgresFixture pg, RedisFixture redisFx)
    {
        _pg = pg;
        _redisFx = redisFx;
    }

    public Task InitializeAsync() => _pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string NewStreamKey() => $"outbox:test:{Guid.NewGuid():N}";

    private async Task<long> OutboxCountAsync(string where)
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        return await conn.ExecuteScalarAsync<long>($"""SELECT count(*) FROM "OutboxMessage" {where}""");
    }

    private async Task EnqueueCommittedAsync(string type)
    {
        var ctx = _pg.CreateDbContext();
        await ctx.BeginAsync();
        await new Outbox(ctx).EnqueueAsync(type, new { });
        await ctx.CommitAsync();
    }

    // ---- (1) enqueue 與交易原子 ----
    [Fact]
    public async Task Enqueue_與交易原子_Commit有列_Rollback沒列()
    {
        var rolledBack = _pg.CreateDbContext();
        await rolledBack.BeginAsync();
        await new Outbox(rolledBack).EnqueueAsync("ConfigChanged", new { a = 1 });
        await rolledBack.RollbackAsync();
        Assert.Equal(0, await OutboxCountAsync(""));

        var committed = _pg.CreateDbContext();
        await committed.BeginAsync();
        await new Outbox(committed).EnqueueAsync("ConfigChanged", new { a = 1 });
        await committed.CommitAsync();
        Assert.Equal(1, await OutboxCountAsync(""));
        Assert.Equal(1, await OutboxCountAsync("""WHERE "ProcessedAt" IS NULL"""));
    }

    // ---- (2) relay：outbox → stream + 標 processed ----
    [Fact]
    public async Task Relay_發布到stream_並標processed()
    {
        await EnqueueCommittedAsync("ConfigChanged");
        using var redis = _redisFx.Connect();
        var streamKey = NewStreamKey();
        var relay = new OutboxRelay(_pg.ConnectionString, redis, NullLogger<OutboxRelay>.Instance, streamKey);

        var n = await relay.RelayBatchAsync(CancellationToken.None);

        Assert.Equal(1, n);
        Assert.Equal(1L, await redis.GetDatabase().StreamLengthAsync(streamKey));    // 已發布到 stream
        Assert.Equal(0, await OutboxCountAsync("""WHERE "ProcessedAt" IS NULL"""));   // outbox 已標 processed
    }

    // ---- (3) SKIP LOCKED：多 relay 不撈同列 ----
    [Fact]
    public async Task SkipLocked_A鎖住_B撈不到同列()
    {
        await EnqueueCommittedAsync("ConfigChanged");
        const string claim = """
            SELECT "Id" FROM "OutboxMessage" WHERE "ProcessedAt" IS NULL
            ORDER BY "Id" FOR UPDATE SKIP LOCKED LIMIT 10
            """;

        await using var connA = new NpgsqlConnection(_pg.ConnectionString);
        await connA.OpenAsync();
        await using var txA = await connA.BeginTransactionAsync();
        Assert.Single((await connA.QueryAsync<long>(claim, transaction: txA)).ToList());

        await using var connB = new NpgsqlConnection(_pg.ConnectionString);
        await connB.OpenAsync();
        await using var txB = await connB.BeginTransactionAsync();
        Assert.Empty((await connB.QueryAsync<long>(claim, transaction: txB)).ToList());

        await txA.RollbackAsync();
    }

    // ---- (4) consumer：消費 → 派 handler → ACK ----
    [Fact]
    public async Task Consumer_消費stream_派handler_並ACK()
    {
        using var redis = _redisFx.Connect();
        var streamKey = NewStreamKey();
        var db = redis.GetDatabase();
        await db.StreamAddAsync(streamKey, new NameValueEntry[] { new("type", "ConfigChanged"), new("payload", "{}") });

        var handler = new RecordingHandler("ConfigChanged");
        var consumer = new OutboxStreamConsumer(redis, new[] { (IOutboxHandler)handler }, NullLogger<OutboxStreamConsumer>.Instance, streamKey);
        await consumer.EnsureGroupAsync();

        var n = await consumer.ConsumeOnceAsync(CancellationToken.None);

        Assert.Equal(1, n);
        Assert.Equal(1, handler.Count);
        var pending = await db.StreamPendingAsync(streamKey, OutboxStream.Group);
        Assert.Equal(0L, pending.PendingMessageCount);   // 已 ACK
    }

    // ---- (5) 失敗留 PEL → 另一 consumer XAUTOCLAIM 重投 ----
    [Fact]
    public async Task Consumer_處理失敗留PEL_另一consumer重投()
    {
        using var redis = _redisFx.Connect();
        var streamKey = NewStreamKey();
        var db = redis.GetDatabase();
        await db.StreamAddAsync(streamKey, new NameValueEntry[] { new("type", "ConfigChanged"), new("payload", "{}") });

        // consumer A：handler 丟例外 → 讀了不 ACK → 留 PEL
        var failing = new ThrowingHandler("ConfigChanged");
        var consumerA = new OutboxStreamConsumer(redis, new[] { (IOutboxHandler)failing }, NullLogger<OutboxStreamConsumer>.Instance,
            streamKey, consumerName: "A");
        await consumerA.EnsureGroupAsync();
        await consumerA.ConsumeOnceAsync(CancellationToken.None);
        Assert.Equal(1L, (await db.StreamPendingAsync(streamKey, OutboxStream.Group)).PendingMessageCount); // 沒 ACK → 還 pending

        // consumer B：claimMinIdle=0 → XAUTOCLAIM 立刻回收 A 的 pending → 成功處理 + ACK
        var ok = new RecordingHandler("ConfigChanged");
        var consumerB = new OutboxStreamConsumer(redis, new[] { (IOutboxHandler)ok }, NullLogger<OutboxStreamConsumer>.Instance,
            streamKey, consumerName: "B", claimMinIdle: TimeSpan.Zero);
        await consumerB.ConsumeOnceAsync(CancellationToken.None);

        Assert.Equal(1, ok.Count);   // 重投給 B、成功
        Assert.Equal(0L, (await db.StreamPendingAsync(streamKey, OutboxStream.Group)).PendingMessageCount); // 已 ACK
    }

    // ---- (6) 無 handler → ACK 丟棄 ----
    [Fact]
    public async Task Consumer_無handler_ACK丟棄不卡PEL()
    {
        using var redis = _redisFx.Connect();
        var streamKey = NewStreamKey();
        var db = redis.GetDatabase();
        await db.StreamAddAsync(streamKey, new NameValueEntry[] { new("type", "UnknownType"), new("payload", "{}") });

        var consumer = new OutboxStreamConsumer(redis, new[] { (IOutboxHandler)new RecordingHandler("ConfigChanged") },
            NullLogger<OutboxStreamConsumer>.Instance, streamKey);
        await consumer.EnsureGroupAsync();
        await consumer.ConsumeOnceAsync(CancellationToken.None);

        Assert.Equal(0L, (await db.StreamPendingAsync(streamKey, OutboxStream.Group)).PendingMessageCount); // 無 handler 也 ACK，不卡 PEL
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

    private sealed class ThrowingHandler : IOutboxHandler
    {
        public ThrowingHandler(string type) => Type = type;
        public string Type { get; }
        public Task HandleAsync(string payload, CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
    }
}
