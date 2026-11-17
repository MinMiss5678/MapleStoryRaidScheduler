using System.Diagnostics;
using Dapper;
using Domain.Exceptions;
using Infrastructure.Dapper;
using Infrastructure.Repositories;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 驗 TeamSlot 編輯的 advisory lock 真的互斥（對真 Postgres）：同一隊伍的併發編輯會序列化。
/// 用 pg_try_advisory_xact_lock（非阻塞）從另一條連線觀察：持鎖時拿不到、釋放後拿得到——確定性、無時序 race。
/// classId 1002 對齊 TeamSlotEditLock.AcquireTeamSlotEditLockAsync。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class TeamSlotEditLockIntegrationTests
{
    private const int LockClass = 1002;
    private readonly PostgresFixture _fx;
    public TeamSlotEditLockIntegrationTests(PostgresFixture fx) => _fx = fx;

    private async Task<bool> TryLockFromOtherConnection(int teamSlotId)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT pg_try_advisory_xact_lock(@c, @t)", new { c = LockClass, t = teamSlotId });
    }

    [Fact]
    public async Task AcquireTeamSlotEditLock_SameTeamSlot_BlocksConcurrent()
    {
        await _fx.ResetAsync();
        const int teamSlotId = 1;

        await using var conn1 = new NpgsqlConnection(_fx.ConnectionString);
        await conn1.OpenAsync(TestContext.Current.CancellationToken);
        var ctx1 = new DbContext(conn1);
        await ctx1.BeginAsync();
        await new TeamSlotEditLock(ctx1).AcquireTeamSlotEditLockAsync(teamSlotId);

        // 持鎖中：另一條連線搶同一隊伍 → 拿不到（互斥成立）
        Assert.False(await TryLockFromOtherConnection(teamSlotId));

        await ctx1.CommitAsync();

        // 釋放後：另一條連線搶得到
        Assert.True(await TryLockFromOtherConnection(teamSlotId));
    }

    // D1（plans/2026-11-16-auth-401-reason-and-commit-before-response.md §四）：交易級鎖在沒有交易時，
    // 那條 SQL 自動提交、鎖立刻釋放，SET LOCAL 也只發 WARNING——防超收的保護默默失效 → 必須直接報錯。
    [Fact]
    public async Task D1_沒有交易時取鎖_丟例外_不得默默放行()
    {
        await _fx.ResetAsync();
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        var ctx = new DbContext(conn); // 刻意不 BeginAsync

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TeamSlotEditLock(ctx).AcquireTeamSlotEditLockAsync(1));
        Assert.Contains("交易", ex.Message);
    }

    [Fact]
    public async Task AcquireTeamSlotEditLock_DifferentTeamSlots_DoNotBlock()
    {
        await _fx.ResetAsync();

        await using var conn1 = new NpgsqlConnection(_fx.ConnectionString);
        await conn1.OpenAsync(TestContext.Current.CancellationToken);
        var ctx1 = new DbContext(conn1);
        await ctx1.BeginAsync();
        await new TeamSlotEditLock(ctx1).AcquireTeamSlotEditLockAsync(1);

        // 不同隊伍（2）不受影響 → 拿得到（不同 objId 互不阻塞、可並行）
        Assert.True(await TryLockFromOtherConnection(2));

        await ctx1.CommitAsync();
    }

    /// <summary>
    /// 驗 lock_timeout 真的有生效（對真 Postgres）：pg_advisory_xact_lock 預設無限期等待，
    /// 持鎖方異常卡住時，後面的請求不該跟著無限期掛住。用短逾時（300ms）確認會在極短時間內
    /// 丟 AdvisoryLockTimeoutException，不是真的等到底。
    /// </summary>
    [Fact]
    public async Task AcquireTeamSlotEditLock_TimesOut_WhenHeldByAnotherTransaction()
    {
        await _fx.ResetAsync();
        const int teamSlotId = 1;

        // A：持鎖不放（模擬持鎖方異常卡住，例如慢查詢或連線異常沒正常 commit/rollback）
        await using var connA = new NpgsqlConnection(_fx.ConnectionString);
        await connA.OpenAsync(TestContext.Current.CancellationToken);
        var ctxA = new DbContext(connA);
        await ctxA.BeginAsync();
        await new TeamSlotEditLock(ctxA).AcquireTeamSlotEditLockAsync(teamSlotId);

        // B：短逾時去搶同一把鎖 → 應該在遠低於「無限等待」的時間內丟例外
        await using var connB = new NpgsqlConnection(_fx.ConnectionString);
        await connB.OpenAsync(TestContext.Current.CancellationToken);
        var ctxB = new DbContext(connB);
        await ctxB.BeginAsync();

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<AdvisoryLockTimeoutException>(() =>
            new TeamSlotEditLock(ctxB, "300ms").AcquireTeamSlotEditLockAsync(teamSlotId));
        sw.Stop();

        // 真的被短 timeout 擋下，不是巧合瞬間成功又被誤判；給寬鬆上限，不是精確計時斷言
        Assert.True(sw.ElapsedMilliseconds < 5000, $"逾時花了 {sw.ElapsedMilliseconds}ms，看起來沒有真的套用短 lock_timeout");

        await ctxB.RollbackAsync();
        await ctxA.CommitAsync();
    }
}
