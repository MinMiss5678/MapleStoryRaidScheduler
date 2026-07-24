using System.Text.Json;
using Application.Interface;
using Infrastructure.Dapper;

namespace Infrastructure.Services;

/// <summary>
/// Outbox 寫入端。用當前 <see cref="DbContext"/> 的交易插入 outbox 列——
/// 因此與同一請求的業務資料**原子提交/回滾**（見 <see cref="IOutbox"/>）。
/// Payload 以 <c>@Payload::jsonb</c> 轉型寫入 jsonb 欄位。
/// </summary>
public class Outbox : IOutbox
{
    private const string InsertSql =
        """INSERT INTO "OutboxMessage" ("Type", "Payload") VALUES (@Type, @Payload::jsonb)""";

    private readonly DbContext _dbContext;

    public Outbox(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task EnqueueAsync(string type, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        // 走 DbContext 的交易（ExecuteAsync 帶 Transaction）→ 與業務資料同一筆交易
        await _dbContext.ExecuteAsync(InsertSql, new { Type = type, Payload = json });
    }
}
