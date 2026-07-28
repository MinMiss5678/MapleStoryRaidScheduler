using Dapper;
using Infrastructure.Dapper;
using Infrastructure.Repositories;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 驗 TeamSlot 編輯的 advisory lock 真的互斥（對真 Postgres）：同一隊伍的併發編輯會序列化。
/// 用 pg_try_advisory_xact_lock（非阻塞）從另一條連線觀察：持鎖時拿不到、釋放後拿得到——確定性、無時序 race。
/// classId 1002 對齊 RegistrationLock.AcquireTeamSlotEditLockAsync。
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
        await conn1.OpenAsync();
        var ctx1 = new DbContext(conn1);
        await ctx1.BeginAsync();
        await new RegistrationLock(ctx1).AcquireTeamSlotEditLockAsync(teamSlotId);

        // 持鎖中：另一條連線搶同一隊伍 → 拿不到（互斥成立）
        Assert.False(await TryLockFromOtherConnection(teamSlotId));

        await ctx1.CommitAsync();

        // 釋放後：另一條連線搶得到
        Assert.True(await TryLockFromOtherConnection(teamSlotId));
    }

    [Fact]
    public async Task AcquireTeamSlotEditLock_DifferentTeamSlots_DoNotBlock()
    {
        await _fx.ResetAsync();

        await using var conn1 = new NpgsqlConnection(_fx.ConnectionString);
        await conn1.OpenAsync();
        var ctx1 = new DbContext(conn1);
        await ctx1.BeginAsync();
        await new RegistrationLock(ctx1).AcquireTeamSlotEditLockAsync(1);

        // 不同隊伍（2）不受影響 → 拿得到（不同 objId 互不阻塞、可並行）
        Assert.True(await TryLockFromOtherConnection(2));

        await ctx1.CommitAsync();
    }
}
