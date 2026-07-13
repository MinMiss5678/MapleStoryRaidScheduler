using Domain.Entities;
using Infrastructure.Repositories;
using Xunit;

namespace Test.Integration;

[Collection("pg")]
[Trait("Category", "Integration")]
public class TeamSlotRepositoryIntegrationTests
{
    private readonly PostgresFixture _fx;
    public TeamSlotRepositoryIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task GetIncompleteTeamsAsync_ReturnsOnlyAutoSourceWithEmptySlot()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs);
        var periodId = await Seed.PeriodAsync(cs,
            new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 8, 0, 0, 0, TimeSpan.Zero));
        var slot = new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);

        var repo = new TeamSlotRepository(_fx.CreateDbContext());

        // auto 隊 + 一個空位（CharacterId null）→ 應被撈到
        var autoId = await repo.CreateAsync(new TeamSlot { BossId = bossId, SlotDateTime = slot, Source = TeamSlotSource.Auto });
        await Seed.EmptySlotAsync(cs, autoId);

        // admin 隊 + 空位 → 不該被撈（合併只吃 Source=auto）
        var adminId = await repo.CreateAsync(new TeamSlot { BossId = bossId, SlotDateTime = slot, Source = TeamSlotSource.Admin });
        await Seed.EmptySlotAsync(cs, adminId);

        var result = (await repo.GetIncompleteTeamsAsync(bossId, periodId)).ToList();

        // foil：只回 auto 隊、admin 隊被排除
        Assert.Single(result);
        var team = result[0];
        Assert.Equal(autoId, team.Id);
        // 順帶驗欄位正確 round-trip（含重構後的 Source 欄位、timestamptz）
        Assert.Equal(bossId, team.BossId);
        Assert.Equal(TeamSlotSource.Auto, team.Source);
        Assert.Equal(slot, team.SlotDateTime);
    }
}
