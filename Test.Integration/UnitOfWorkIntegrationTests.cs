using Dapper;
using Domain.Entities;
using Infrastructure.Dapper;
using Infrastructure.Repositories;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 驗真交易邊界（不是 mock）：真的 UnitOfWork + 真的 TeamSlotRepository 共用同一連線/交易。
/// 對真 Postgres 證明 Commit 持久且隔離、Rollback 跨表原子撤銷——這正是 UoW 在 request 邊界做的事。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class UnitOfWorkIntegrationTests
{
    private readonly PostgresFixture _fx;
    public UnitOfWorkIntegrationTests(PostgresFixture fx) => _fx = fx;

    private static readonly DateTimeOffset Slot = new(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Commit_PersistsWrites_AndOtherConnectionSeesNothingUntilCommit()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs);

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var ctx = new DbContext(conn);
        var uow = new UnitOfWork(ctx);
        var repo = new TeamSlotRepository(ctx);

        await uow.BeginAsync();
        var teamId = await repo.CreateAsync(new TeamSlot { BossId = bossId, SlotDateTime = Slot, Source = TeamSlotSource.Auto });

        // 提交前：另一條連線在 READ COMMITTED 下看不到未提交資料（證明是真交易、有隔離）
        var visibleBeforeCommit = await Seed.CountTeamAsync(cs, teamId);

        await uow.CommitAsync();

        // 提交後：另一條連線才看得到（持久化）
        var visibleAfterCommit = await Seed.CountTeamAsync(cs, teamId);

        Assert.Equal(0, visibleBeforeCommit);
        Assert.Equal(1, visibleAfterCommit);
    }

    [Fact]
    public async Task Rollback_DiscardsWritesAcrossTables_Atomically()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs);

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var ctx = new DbContext(conn);
        var uow = new UnitOfWork(ctx);
        var repo = new TeamSlotRepository(ctx);

        await uow.BeginAsync();
        // 同一交易內寫兩張表：TeamSlot（透過 repo）+ TeamSlotCharacter（透過同一 ctx）
        var teamId = await repo.CreateAsync(new TeamSlot { BossId = bossId, SlotDateTime = Slot, Source = TeamSlotSource.Auto });
        await ctx.ExecuteAsync("""INSERT INTO "TeamSlotCharacter"("TeamSlotId","Job") VALUES (@id,'-');""", new { id = teamId });

        await uow.RollbackAsync();

        // foil：兩張表都必須全數回滾，不能只回一張（原子性）
        var teamCount = await Seed.CountTeamAsync(cs, teamId);
        var charCount = await ScalarAsync(cs, """SELECT COUNT(*) FROM "TeamSlotCharacter" WHERE "TeamSlotId" = @id;""", new { id = teamId });

        Assert.Equal(0, teamCount);
        Assert.Equal(0, charCount);
    }

    private static async Task<int> ScalarAsync(string cs, string sql, object param)
    {
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        return await c.ExecuteScalarAsync<int>(sql, param);
    }
}
