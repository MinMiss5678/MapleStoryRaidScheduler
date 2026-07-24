namespace Infrastructure.BackgroundJobs;

/// <summary>Outbox → Redis Streams 的 key / consumer group 名稱（relay 與 consumer 共用）。</summary>
public static class OutboxStream
{
    public const string Key = "outbox:stream";
    public const string Group = "outbox-consumers";
}
