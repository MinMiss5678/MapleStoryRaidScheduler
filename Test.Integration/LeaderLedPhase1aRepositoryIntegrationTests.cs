using Domain.Entities;
using Infrastructure.Repositories;
using Xunit;

namespace Test.Integration;

/// <summary>
/// leader-led Phase 1a 新表 repository 的 round-trip（寫→讀）整合測試，跑在真 Postgres（含 000009 migration）。
/// 只驗資料層實作正確，尚無業務行為（1b/1c 才接消費者）。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class LeaderLedPhase1aRepositoryIntegrationTests
{
    private readonly PostgresFixture _fx;
    public LeaderLedPhase1aRepositoryIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task TeamSlotRequirement_CreateWithJobs_GetByTeamSlotId_RoundTrips()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs);
        var teamSlotId = await Seed.TeamSlotAsync(cs, bossId, "admin");

        var repo = new TeamSlotRequirementRepository(_fx.CreateDbContext());

        // 「箭神(≥900) or 槍神(≥1000) 1位、須打過本王(通關≥1)」
        var reqId = await repo.CreateAsync(new TeamSlotRequirement
        {
            TeamSlotId = teamSlotId,
            Count = 1,
            MinClearCount = 1,
            Jobs =
            [
                new TeamSlotRequirementJob { Job = "箭神", MinAttackPower = 900 },
                new TeamSlotRequirementJob { Job = "槍神", MinAttackPower = 1000 },
            ]
        });
        Assert.True(reqId > 0);

        var loaded = (await repo.GetByTeamSlotIdAsync(teamSlotId)).ToList();

        Assert.Single(loaded);
        var req = loaded[0];
        Assert.Equal(teamSlotId, req.TeamSlotId);
        Assert.Equal(1, req.Count);
        Assert.Equal(1, req.MinClearCount);
        Assert.Equal(2, req.Jobs.Count);
        Assert.Contains(req.Jobs, j => j.Job == "箭神" && j.MinAttackPower == 900);
        Assert.Contains(req.Jobs, j => j.Job == "槍神" && j.MinAttackPower == 1000);
    }

    [Fact]
    public async Task TeamSlotRequirement_DeleteByTeamSlotId_CascadesJobs()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs);
        var teamSlotId = await Seed.TeamSlotAsync(cs, bossId, "admin");

        var repo = new TeamSlotRequirementRepository(_fx.CreateDbContext());
        await repo.CreateAsync(new TeamSlotRequirement
        {
            TeamSlotId = teamSlotId,
            Count = 2,
            Jobs = [new TeamSlotRequirementJob { Job = "主教", MinAttackPower = 0 }]
        });

        await new TeamSlotRequirementRepository(_fx.CreateDbContext()).DeleteByTeamSlotIdAsync(teamSlotId);

        var loaded = await new TeamSlotRequirementRepository(_fx.CreateDbContext()).GetByTeamSlotIdAsync(teamSlotId);
        Assert.Empty(loaded);  // 需求列沒了；子表 Job 由 FK CASCADE 連帶清（無殘留才讀得回空）
    }

    [Fact]
    public async Task CharacterBossClear_Create_GetByCharacterId_RoundTrips()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs);
        await Seed.PlayerAsync(cs, 555, "P");
        await Seed.CharacterAsync(cs, "c555", 555, "角色", "夜使者", 1200);

        var repo = new CharacterBossClearRepository(_fx.CreateDbContext());
        await repo.CreateAsync(new CharacterBossClear { CharacterId = "c555", BossId = bossId, ClearCount = 5 });

        var loaded = (await repo.GetByCharacterIdAsync("c555")).ToList();

        Assert.Single(loaded);
        Assert.Equal("c555", loaded[0].CharacterId);
        Assert.Equal(bossId, loaded[0].BossId);
        Assert.Equal(5, loaded[0].ClearCount);
    }
}
