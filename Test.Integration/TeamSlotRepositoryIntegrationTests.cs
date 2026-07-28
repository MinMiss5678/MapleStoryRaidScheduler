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

    /// <summary>
    /// 釘住 Phase 2 的關鍵事實：UpdateAsync 是「整組砍掉重灌」（DELETE 全部成員列 + 重新 INSERT），
    /// 不是逐列 UPDATE。合併後角色列會拿到全新的 Id；IsManual 等欄位需正確存活。
    /// </summary>
    [Fact]
    public async Task UpdateAsync_DeletesAndReinsertsAllCharacters_AssigningNewIds()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs);
        var repo = new TeamSlotRepository(_fx.CreateDbContext());

        var teamId = await repo.CreateAsync(new TeamSlot
        {
            BossId = bossId,
            SlotDateTime = new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero),
            Source = TeamSlotSource.Auto
        });
        await Seed.OccupiedSlotAsync(cs, teamId, discordId: 111, charId: "occ1");

        var team = await repo.GetByIdAsync(teamId);
        Assert.NotNull(team);
        Assert.Single(team!.Characters);
        var originalRowId = team.Characters[0].Id;

        // 模擬合併：既有成員 + 新成員（沿用聚合的 AbsorbMembers 語意）一起整組覆蓋
        await Seed.PlayerAsync(cs, 222, "P2");
        await Seed.CharacterAsync(cs, "occ2", 222, "C2", "Bishop", 800);
        var newMember = new TeamSlotCharacter
        {
            DiscordId = 222,
            DiscordName = "P2",
            CharacterId = "occ2",
            Job = "Bishop",
            IsManual = true
        };
        team.Capacity = 6;
        team.AbsorbMembers(new[] { newMember }, new DateTimeOffset(2026, 4, 3, 20, 0, 0, TimeSpan.Zero));

        await repo.UpdateAsync(team);

        var reloaded = await repo.GetByIdAsync(teamId);
        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded!.Characters.Count);
        Assert.Equal(new DateTimeOffset(2026, 4, 3, 20, 0, 0, TimeSpan.Zero), reloaded.SlotDateTime);

        // 核心事實：原本那筆成員列被砍掉重灌，拿到全新 Id（不是原地 UPDATE）
        var survivedOriginal = reloaded.Characters.FirstOrDefault(c => c.CharacterId == "occ1");
        Assert.NotNull(survivedOriginal);
        Assert.NotEqual(originalRowId, survivedOriginal!.Id);

        // 新成員的 IsManual 正確存活
        var newRow = reloaded.Characters.FirstOrDefault(c => c.CharacterId == "occ2");
        Assert.NotNull(newRow);
        Assert.True(newRow!.IsManual);
    }
}
