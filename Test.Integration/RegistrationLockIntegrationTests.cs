using Dapper;
using Infrastructure.Dapper;
using Infrastructure.Repositories;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 驗 advisory lock 真的互斥（對真 Postgres）：同一 period 的自動排隊會序列化 → 併發報名不會各開一隊。
/// 用 pg_try_advisory_xact_lock（非阻塞）從另一條連線觀察：持鎖時拿不到、釋放後拿得到——確定性、無時序 race。
/// classId 1001 對齊 RegistrationLock。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class RegistrationLockIntegrationTests
{
    private const int LockClass = 1001;
    private readonly PostgresFixture _fx;
    public RegistrationLockIntegrationTests(PostgresFixture fx) => _fx = fx;

    // 另一條連線嘗試搶同一把 xact advisory lock，回傳能否拿到
    private async Task<bool> TryLockFromOtherConnection(int periodId)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT pg_try_advisory_xact_lock(@c, @p)", new { c = LockClass, p = periodId });
    }

    [Fact]
    public async Task AcquireAutoAssignLock_SamePeriod_BlocksConcurrent()
    {
        await _fx.ResetAsync();
        const int periodId = 1;

        // conn1：開交易 + 取鎖（走受測的 RegistrationLock）
        await using var conn1 = new NpgsqlConnection(_fx.ConnectionString);
        await conn1.OpenAsync();
        var ctx1 = new DbContext(conn1);
        await ctx1.BeginAsync(); // advisory_xact_lock 需在交易內
        await new RegistrationLock(ctx1).AcquireAutoAssignLockAsync(periodId);

        // 持鎖中：另一條連線搶同一 period → 拿不到（互斥成立）
        Assert.False(await TryLockFromOtherConnection(periodId));

        // conn1 交易提交 → 鎖自動釋放
        await ctx1.CommitAsync();

        // 釋放後：另一條連線搶得到
        Assert.True(await TryLockFromOtherConnection(periodId));
    }

    [Fact]
    public async Task AcquireAutoAssignLock_DifferentPeriods_DoNotBlock()
    {
        await _fx.ResetAsync();

        // conn1 鎖 period 1
        await using var conn1 = new NpgsqlConnection(_fx.ConnectionString);
        await conn1.OpenAsync();
        var ctx1 = new DbContext(conn1);
        await ctx1.BeginAsync();
        await new RegistrationLock(ctx1).AcquireAutoAssignLockAsync(1);

        // 不同 period（2）不受影響 → 拿得到（不同 objId 互不阻塞、可並行）
        Assert.True(await TryLockFromOtherConnection(2));

        await ctx1.CommitAsync();
    }
}
