using Application.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// 從 Redis Stream 消費 outbox 事件，依 <c>type</c> 派給 <see cref="IOutboxHandler"/>，處理成功 <c>XACK</c>。
///
/// 設計：
/// - <b>consumer group</b>：多 consumer（多 pod）競爭消費、各分不相交訊息（免選 leader）。
/// - <b>at-least-once</b>：處理成功才 ACK；處理中/ACK 前崩 → 訊息留在 PEL（Pending Entries List）→
///   由 <c>XAUTOCLAIM</c> 回收逾時 pending 重投。handler 需冪等吸收重複。
/// - 無對應 handler → ACK 丟棄（永遠處理不了，避免卡 PEL）+ 記 log。
/// </summary>
public class OutboxStreamConsumer : BackgroundService
{
    private const int BatchSize = 10;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IConnectionMultiplexer _redis;
    private readonly IReadOnlyDictionary<string, IOutboxHandler> _handlers;
    private readonly ILogger<OutboxStreamConsumer> _logger;
    private readonly string _streamKey;
    private readonly string _groupName;
    private readonly string _consumerName;
    private readonly TimeSpan _claimMinIdle; // 只回收 pending 逾此時間的（判定 consumer 已死）

    public OutboxStreamConsumer(
        IConnectionMultiplexer redis, IEnumerable<IOutboxHandler> handlers, ILogger<OutboxStreamConsumer> logger,
        string? streamKey = null, string? groupName = null, string? consumerName = null, TimeSpan? claimMinIdle = null)
    {
        _redis = redis;
        _handlers = handlers.ToDictionary(h => h.Type);
        _logger = logger;
        _streamKey = streamKey ?? OutboxStream.Key;
        _groupName = groupName ?? OutboxStream.Group;
        _consumerName = consumerName ?? $"{Environment.MachineName}-{Guid.NewGuid():N}";
        _claimMinIdle = claimMinIdle ?? TimeSpan.FromSeconds(30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxStreamConsumer is starting.");
        await EnsureGroupAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var handled = await ConsumeOnceAsync(stoppingToken);
                if (handled >= BatchSize)
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxStreamConsumer loop failed");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal async Task EnsureGroupAsync()
    {
        var db = _redis.GetDatabase();
        try
        {
            // 從 stream 起點建 group（createStream=true）→ 不漏 group 建立前已發布的訊息
            await db.StreamCreateConsumerGroupAsync(_streamKey, _groupName, StreamPosition.Beginning, createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // group 已存在，無妨
        }
    }

    /// <summary>跑一輪：先回收逾時 pending（崩掉 consumer 的），再讀新訊息。回傳處理筆數。internal 供整合測。</summary>
    internal async Task<int> ConsumeOnceAsync(CancellationToken ct)
    {
        var db = _redis.GetDatabase();

        // 1) 回收 pending 逾時（別的 consumer 讀了沒 ACK 就崩）→ at-least-once 的重投來源
        var claimed = await db.StreamAutoClaimAsync(
            _streamKey, _groupName, _consumerName, (long)_claimMinIdle.TotalMilliseconds, StreamPosition.Beginning, BatchSize);
        var count = await ProcessEntriesAsync(db, claimed.ClaimedEntries, ct);

        // 2) 讀本 consumer 的新訊息（">" = 尚未投遞給任何 consumer 的）
        var entries = await db.StreamReadGroupAsync(_streamKey, _groupName, _consumerName, ">", BatchSize);
        count += await ProcessEntriesAsync(db, entries, ct);

        return count;
    }

    private async Task<int> ProcessEntriesAsync(IDatabase db, StreamEntry[] entries, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            var type = entry["type"].ToString();
            var payload = entry["payload"].ToString();

            if (!_handlers.TryGetValue(type, out var handler))
            {
                _logger.LogWarning("Stream 無對應 handler，ACK 丟棄 id={Id} type={Type}", entry.Id, type);
                await db.StreamAcknowledgeAsync(_streamKey, _groupName, entry.Id);
                continue;
            }

            try
            {
                await handler.HandleAsync(payload, ct);
                await db.StreamAcknowledgeAsync(_streamKey, _groupName, entry.Id); // 成功才 ACK
            }
            catch (Exception ex)
            {
                // 不 ACK → 留在 PEL → 逾時後被 XAUTOCLAIM 回收重投（handler 冪等吸收）
                _logger.LogWarning(ex, "Stream 投遞失敗，留 PEL 待重投 id={Id} type={Type}", entry.Id, type);
            }
        }
        return entries.Length;
    }
}
