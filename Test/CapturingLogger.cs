using Microsoft.Extensions.Logging;

namespace Test;

/// <summary>測試用 logger：記下每一筆 log 的等級、格式化後的訊息與結構化參數，供斷言。</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> State);

    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var values = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
        Entries.Add(new Entry(logLevel, formatter(state, exception), values));
    }

    /// <summary>全部 log（訊息＋參數值）串成一段文字，方便斷言「不得出現某個值」。</summary>
    public string AllText() =>
        string.Join("\n", Entries.Select(e => e.Message + " | " + string.Join(",", e.State.Select(kv => kv.Value))));
}
