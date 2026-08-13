using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Presentation.WebApi.HealthChecks;

/// <summary>
/// Readiness（就緒）探針用：開一條獨立連線，從核心表 "Boss" 取一筆，確認「DB 連得到 + schema 已套用 + 真查詢可跑」。
/// 比純 SELECT 1 多守一層——migration 沒跑 / 表不存在也會被抓成 503（部署時 rollout 等這個探針 → 等於一個輕量 smoke）。
/// （period-less 4d：原本碰 "Period"，該表已退場 → 改碰同為核心且長存的 "Boss"。）
/// LIMIT 1 且不判斷有無資料 → 空表仍算健康，避免新環境資料未進就誤判 unhealthy、擋住 rollout。
/// 刻意不重用 request-scoped 的 UoW 連線——那條綁交易、生命週期是每請求，
/// 健康檢查要的是「現在還連不連得上」，用短命連線 + try/catch 把 DB 掛掉轉成 503 而非 500。
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public DatabaseHealthCheck(string connectionString) => _connectionString = connectionString;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            // 碰核心表（非純 SELECT 1）→ 驗 schema/migration 已套用且表可查詢；LIMIT 1、不判斷筆數 → 空表也健康
            await using var cmd = new NpgsqlCommand("SELECT 1 FROM \"Boss\" LIMIT 1", conn);
            await cmd.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("資料庫連線與核心表查詢正常");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("資料庫連線失敗", ex);
        }
    }
}
