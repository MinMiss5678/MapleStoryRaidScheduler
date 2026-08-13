using Infrastructure.Query;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 驗 TeamSlotQuery.GetBySlotDateTimeAsync 的多 LEFT JOIN（TeamSlot→TeamSlotCharacter→Boss）
/// + 當日時間窗過濾（[slot, slot+1day)）+ BossName 由 Boss JOIN 帶出。period-less（4d）後只剩此讀法。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class TeamSlotQueryIntegrationTests
{
    private readonly PostgresFixture _fx;
    public TeamSlotQueryIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task GetBySlotDateTimeAsync_JoinBossName_且濾出當日時間窗()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs, "DEMO王");

        var slot = new DateTimeOffset(2026, 4, 3, 12, 0, 0, TimeSpan.Zero);
        var sameDay = slot.AddHours(3);          // 落在 [slot, slot+1day) 窗內
        var nextDay = slot.AddDays(2);           // 窗外

        // 窗內 + 含 1 成員 → 應回
        var tsIn = await Seed.TeamSlotAsync(cs, bossId, "leader", sameDay);
        await Seed.OccupiedSlotAsync(cs, tsIn, 111, "occ1");
        // 窗外 → 應被過濾
        var tsOut = await Seed.TeamSlotAsync(cs, bossId, "leader", nextDay);
        await Seed.OccupiedSlotAsync(cs, tsOut, 222, "occ2");

        var query = new TeamSlotQuery(_fx.CreateDbContext());

        var rows = (await query.GetBySlotDateTimeAsync(slot)).ToList();

        // foil：只回窗內隊；BossName 由 Boss LEFT JOIN、成員 occ1 由 TeamSlotCharacter JOIN 帶出
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(tsIn, r.TeamSlotId));
        Assert.Contains(rows, r => r.BossName == "DEMO王" && r.CharacterId == "occ1");
    }
}
