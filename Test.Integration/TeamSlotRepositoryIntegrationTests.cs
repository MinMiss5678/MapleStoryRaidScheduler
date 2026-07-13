using Dapper;
using Domain.Entities;
using Infrastructure.Repositories;
using Npgsql;
using Xunit;

namespace Test.Integration;

[Collection("pg")]
[Trait("Category", "Integration")]
public class TeamSlotRepositoryIntegrationTests
{
    private readonly PostgresFixture _fx;
    public TeamSlotRepositoryIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task CreateAsync_ThenGetById_RoundTrips()
    {
        await _fx.ResetAsync();
        var bossId = await InsertBossAsync();

        var repo = new TeamSlotRepository(_fx.CreateDbContext());
        var id = await repo.CreateAsync(new TeamSlot
        {
            BossId = bossId,
            SlotDateTime = new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero),
            Source = TeamSlotSource.Admin
        });

        var loaded = await repo.GetByIdAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal(bossId, loaded!.BossId);
        Assert.Equal(TeamSlotSource.Admin, loaded.Source);
        Assert.Equal(new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero), loaded.SlotDateTime);
    }

    [Fact]
    public async Task GetIncompleteTeamsAsync_ReturnsOnlyAutoSourceWithEmptySlot()
    {
        await _fx.ResetAsync();
        var bossId = await InsertBossAsync();
        var periodId = await InsertPeriodAsync(
            new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 8, 0, 0, 0, TimeSpan.Zero));
        var slot = new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);

        var repo = new TeamSlotRepository(_fx.CreateDbContext());

        // auto 隊 + 一個空位（CharacterId null）→ 應被撈到
        var autoId = await repo.CreateAsync(new TeamSlot { BossId = bossId, SlotDateTime = slot, Source = TeamSlotSource.Auto });
        await InsertEmptySlotAsync(autoId);

        // admin 隊 + 空位 → 不該被撈（合併只吃 Source=auto）
        var adminId = await repo.CreateAsync(new TeamSlot { BossId = bossId, SlotDateTime = slot, Source = TeamSlotSource.Admin });
        await InsertEmptySlotAsync(adminId);

        var result = (await repo.GetIncompleteTeamsAsync(bossId, periodId)).ToList();

        Assert.Single(result);
        Assert.Equal(autoId, result[0].Id);
    }

    // --- FK 前置資料（用原生 SQL）---

    private async Task<int> InsertBossAsync()
    {
        await using var c = new NpgsqlConnection(_fx.ConnectionString);
        await c.OpenAsync();
        return await c.ExecuteScalarAsync<int>(
            """INSERT INTO "Boss"("Name","RequireMembers","RoundConsumption") VALUES ('DEMO',6,1) RETURNING "Id";""");
    }

    private async Task<int> InsertPeriodAsync(DateTimeOffset s, DateTimeOffset e)
    {
        await using var c = new NpgsqlConnection(_fx.ConnectionString);
        await c.OpenAsync();
        return await c.ExecuteScalarAsync<int>(
            """INSERT INTO "Period"("StartDate","EndDate") VALUES (@s,@e) RETURNING "Id";""", new { s, e });
    }

    private async Task InsertEmptySlotAsync(int teamSlotId)
    {
        await using var c = new NpgsqlConnection(_fx.ConnectionString);
        await c.OpenAsync();
        await c.ExecuteAsync(
            """INSERT INTO "TeamSlotCharacter"("TeamSlotId","Job") VALUES (@teamSlotId,'-');""", new { teamSlotId });
    }
}
