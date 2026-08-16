using Dapper;
using Infrastructure.Dapper;
using Infrastructure.Repositories;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 驗 advisory lock 真的互斥（對真 Postgres）：同一隊伍的入隊定案（ConfirmMember）會序列化 → 併發接受/核准不會超編。
/// 用 pg_try_advisory_xact_lock（非阻塞）從另一條連線觀察：持鎖時拿不到、釋放後拿得到——確定性、無時序 race。
/// classId 1002 對齊 RegistrationLock.AcquireTeamSlotEditLockAsync。
/// （period-less 4d 後 classId 1001 的 auto-assign 鎖已退場；同一把序列化機制由 1002 edit 鎖承接。）
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class RegistrationLockIntegrationTests
{
    private const int LockClass = 1002;
    private readonly PostgresFixture _fx;
    public RegistrationLockIntegrationTests(PostgresFixture fx) => _fx = fx;

    // 另一條連線嘗試搶同一把 xact advisory lock，回傳能否拿到
    private async Task<bool> TryLockFromOtherConnection(int teamSlotId)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT pg_try_advisory_xact_lock(@c, @t)", new { c = LockClass, t = teamSlotId });
    }

    [Fact]
    public async Task AcquireTeamSlotEditLock_SameTeam_BlocksConcurrent()
    {
        await _fx.ResetAsync();
        const int teamSlotId = 1;

        // conn1：開交易 + 取鎖（走受測的 RegistrationLock）
        await using var conn1 = new NpgsqlConnection(_fx.ConnectionString);
        await conn1.OpenAsync();
        var ctx1 = new DbContext(conn1);
        await ctx1.BeginAsync(); // advisory_xact_lock 需在交易內
        await new RegistrationLock(ctx1).AcquireTeamSlotEditLockAsync(teamSlotId);

        // 持鎖中：另一條連線搶同一隊 → 拿不到（互斥成立）
        Assert.False(await TryLockFromOtherConnection(teamSlotId));

        // conn1 交易提交 → 鎖自動釋放
        await ctx1.CommitAsync();

        // 釋放後：另一條連線搶得到
        Assert.True(await TryLockFromOtherConnection(teamSlotId));
    }

    [Fact]
    public async Task AcquireTeamSlotEditLock_DifferentTeams_DoNotBlock()
    {
        await _fx.ResetAsync();

        // conn1 鎖 teamSlot 1
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
