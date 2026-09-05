using System.Text.Json;
using Application.Interface;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// 多 node HA demo 專用的 dry-run handler（plans/2026-09-04-multi-pod-outbox-dispatch.md §3）：
/// 只認 <see cref="DemoType"/> 的種子列，dispatcher claim 後「<b>不真送 DM</b>」——僅記一行結構化 log
/// （序號 n + pod 主機名 + node 名）。用途：多 pod 跨 node 併發時，彙總各 pod 的 log 斷言
/// 「每個 n 恰出現一次」＝ FOR UPDATE SKIP LOCKED 在真分散式下 exactly-once，且不洗爆使用者、
/// 不吃滿 Discord per-token rate limit（N 筆真送會兩者皆中）。
/// <para>
/// 僅在 <c>Dispatch:DryRun=true</c> 時註冊（見 Presentation/Program.cs）→ prod 完全不載入、零影響；
/// 即使誤註冊也只認 <see cref="DemoType"/>（prod 永無此型別列），與 TeamNotification handler 不同 key、不衝突。
/// </para>
/// </summary>
public sealed class DryRunOutboxHandler : IOutboxHandler
{
    /// <summary>HA demo 種子列的 outbox 型別（seed 用同一字串）。</summary>
    public const string DemoType = "HaDemo";

    private readonly ILogger<DryRunOutboxHandler> _logger;
    // pod 主機名（k8s 預設把 HOSTNAME 設為 pod 名）＋ node 名（由 Deployment 用 downward API 注入 NODE_NAME）。
    private readonly string _pod = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
    private readonly string _node = Environment.GetEnvironmentVariable("NODE_NAME") ?? "?";

    public DryRunOutboxHandler(ILogger<DryRunOutboxHandler> logger) => _logger = logger;

    public string Type => DemoType;

    public Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var n = JsonDocument.Parse(payload).RootElement.TryGetProperty("n", out var v) ? v.GetInt32() : -1;
        // 固定前綴 HADEMO_CLAIM → 彙總時 grep 出來、依 n 排序 uniq 驗每列恰一次；pod/node 供「真跨機器」佐證。
        _logger.LogInformation("HADEMO_CLAIM n={N} pod={Pod} node={Node}", n, _pod, _node);
        return Task.CompletedTask;
    }
}
