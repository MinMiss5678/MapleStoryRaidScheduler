using System.Collections.Concurrent;
using System.Text.Json;
using Application.Interface;
using Dapper;
using Infrastructure.BackgroundJobs;
using Infrastructure.Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// N×M 併發派發正確性（多 pod outbox dispatch，plans/2026-09-04-multi-pod-outbox-dispatch.md）：
/// 起 M 個「真」<see cref="OutboxDispatcher"/>（各自 NpgsqlConnectionFactory＝各自連線，等同 M 個 pod）
/// 併發搶 N 筆已提交 outbox，只靠 <c>FOR UPDATE SKIP LOCKED</c>（無 Leader Election）證：
///   (1) 快樂路徑＝<b>恰一次</b>（無重複、無遺漏）；
///   (2) chaos（處理中殺一個 dispatcher）＝<b>at-least-once</b>（被鎖未提交列隨 tx rollback 釋放、被別的 pod 接手，最終無遺漏）。
/// 這是 DB 層性質、與機器數無關 → 本機 M 併發即足以證明，不需真多節點。
/// </summary>
[Collection("pg")]
public class OutboxConcurrencyIntegrationTests : IAsyncLifetime
{
    private const string Type = "ConcTest";

    private readonly PostgresFixture _fx;
    public OutboxConcurrencyIntegrationTests(PostgresFixture fx) => _fx = fx;

    public Task InitializeAsync() => _fx.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task M個dispatcher併發搶N列_每列恰處理一次_無重複無遺漏()
    {
        const int n = 400;
        const int m = 6;
        await SeedAsync(n);

        var sink = new ConcurrentSink(Type);
        // M 個獨立 dispatcher：各自的 connection factory → 各開各的連線，等同 M 個 pod 併跑。共用 sink 記錄。
        var dispatchers = Enumerable.Range(0, m)
            .Select(_ => new OutboxDispatcher(new NpgsqlConnectionFactory(_fx.ConnectionString),
                new[] { (IOutboxHandler)sink }, NullLogger<OutboxDispatcher>.Instance))
            .ToArray();

        await Task.WhenAll(dispatchers.Select(d => DrainAsync(d, CancellationToken.None)));

        // 恰一次：處理總次數＝N（無重複）、涵蓋的 id 集合＝{0..N-1}（無遺漏）、DB 無殘留未處理列。
        Assert.Equal(0, await UnprocessedCountAsync());
        Assert.Equal(n, sink.Seen.Count);                                   // 無重複（否則 > N）
        Assert.Equal(n, sink.Seen.Distinct().Count());                      // 無遺漏
        Assert.Equal(Enumerable.Range(0, n), sink.Seen.OrderBy(x => x));    // 恰好每列一次
    }

    [Fact]
    public async Task Chaos_處理中殺一個dispatcher_未提交列被他人接手_最終無遺漏()
    {
        const int n = 400;
        const int m = 5;
        await SeedAsync(n);

        var sink = new ConcurrentSink(Type);
        var factory = () => new OutboxDispatcher(new NpgsqlConnectionFactory(_fx.ConnectionString),
            new[] { (IOutboxHandler)sink }, NullLogger<OutboxDispatcher>.Instance);

        // 「被殺的 pod」：處理途中取消其 token → 進行中的 batch（已 claim、handler 已跑、尚未 commit）
        // 隨 tx dispose 而 rollback → 被鎖的列釋放，回到未處理，供其他 pod 接手。
        using var victimCts = new CancellationTokenSource();
        var victim = RunUntilCancelledAsync(factory(), victimCts.Token);

        // 其他存活 pod
        var survivors = Enumerable.Range(0, m - 1)
            .Select(_ => DrainAsync(factory(), CancellationToken.None))
            .ToArray();

        // 讓 victim 先搶到並處理一些，再殺掉
        await Task.Delay(40);
        victimCts.Cancel();

        await victim;                        // 吞掉取消、結束
        await Task.WhenAll(survivors);       // 存活者把剩下（含 victim 釋放的）全數處理完

        // at-least-once + 無遺漏：涵蓋所有 id（無遺漏）、DB 無殘留；總次數 ≥ N（victim rollback 的列可能被重送）。
        Assert.Equal(0, await UnprocessedCountAsync());
        Assert.Equal(n, sink.Seen.Distinct().Count());                      // 無遺漏（核心保證）
        Assert.Equal(Enumerable.Range(0, n), sink.Seen.Distinct().OrderBy(x => x));
        Assert.True(sink.Seen.Count >= n, $"至少一次：實得 {sink.Seen.Count} < {n}");
    }

    // 反覆跑批直到全表無未處理列（併發下某輪 0 不代表結束——別的 pod 可能持鎖中，稍等再試）。
    private async Task DrainAsync(OutboxDispatcher dispatcher, CancellationToken ct)
    {
        while (true)
        {
            var processed = await dispatcher.ProcessBatchAsync(ct);
            if (processed == 0)
            {
                if (await UnprocessedCountAsync() == 0) break;
                await Task.Delay(10, ct);
            }
        }
    }

    // victim：一直跑批直到自己的 token 被取消（模擬 pod 被殺）。取消可能落在 commit 前 → 該 batch rollback。
    private async Task RunUntilCancelledAsync(OutboxDispatcher dispatcher, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
                await dispatcher.ProcessBatchAsync(ct);
        }
        catch (OperationCanceledException) { /* 被殺：進行中 batch 未 commit → 釋鎖 */ }
    }

    private async Task SeedAsync(int n)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        // 批量塞 N 筆已提交、未處理列；payload 帶唯一序號 n，供 sink 追蹤「哪一列被處理、幾次」。
        await conn.ExecuteAsync(
            """
            INSERT INTO "OutboxMessage" ("Type", "Payload")
            SELECT @Type, jsonb_build_object('n', g)
            FROM generate_series(0, @Max) AS g;
            """,
            new { Type, Max = n - 1 });
    }

    private async Task<long> UnprocessedCountAsync()
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        return await conn.ExecuteScalarAsync<long>(
            """SELECT count(*) FROM "OutboxMessage" WHERE "ProcessedAt" IS NULL""");
    }

    /// <summary>thread-safe 記錄 handler：多 dispatcher 共用一個，記下每次處理的 payload 序號（重複＝同號多次）。</summary>
    private sealed class ConcurrentSink : IOutboxHandler
    {
        private readonly ConcurrentQueue<int> _seen = new();
        public ConcurrentSink(string type) => Type = type;
        public string Type { get; }
        public IReadOnlyCollection<int> Seen => _seen;

        public Task HandleAsync(string payload, CancellationToken cancellationToken)
        {
            var n = JsonDocument.Parse(payload).RootElement.GetProperty("n").GetInt32();
            _seen.Enqueue(n);
            return Task.CompletedTask;
        }
    }
}
