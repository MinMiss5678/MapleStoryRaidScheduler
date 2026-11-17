using System.Text.Json;
using Application.Interface;
using Infrastructure.Dapper;

namespace Infrastructure.Services;

/// <summary>
/// Outbox 寫入端。用當前 <see cref="DbContext"/> 的交易插入 outbox 列——
/// 因此與同一請求的業務資料**原子提交/rollback**（見 <see cref="IOutbox"/>）。
/// Payload 以 <c>@Payload::jsonb</c> 轉型寫入 jsonb 欄位。
/// </summary>
public class Outbox : IOutbox
{
    private const string InsertSql =
        """INSERT INTO "OutboxMessage" ("Type", "Payload", "DeliverBefore") VALUES (@Type, @Payload::jsonb, @DeliverBefore)""";

    private readonly DbContext _dbContext;

    public Outbox(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task EnqueueAsync(string type, object payload, DateTimeOffset? deliverBefore = null)
    {
        // outbox 的意義就是「與業務資料同一筆交易原子提交」；沒有交易時會自動提交、與資料脫鉤 → 直接報錯
        if (_dbContext.Transaction == null)
            throw new InvalidOperationException(
                "Outbox 必須在交易內寫入：目前沒有進行中的交易，通知會與業務資料脫鉤。");

        var json = JsonSerializer.Serialize(payload);
        // 走 DbContext 的交易（ExecuteAsync 帶 Transaction）→ 與業務資料同一筆交易
        await _dbContext.ExecuteAsync(InsertSql, new { Type = type, Payload = json, DeliverBefore = deliverBefore });
    }
}
