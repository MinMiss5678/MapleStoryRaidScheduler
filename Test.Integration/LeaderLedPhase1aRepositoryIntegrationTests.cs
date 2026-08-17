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
    public async Task LfgIntentCleanupJob_DeletesExpired_KeepsActive()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        await Seed.PlayerAsync(cs, 999, "P999");
        await Seed.CharacterAsync(cs, "c999", 999, "C", "英雄", 900);
        var bossId = await Seed.BossAsync(cs);
        await using (var conn = new Npgsql.NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            // 兩筆需在 (CharacterId,BossId) 不同組，否則撞 000020 唯一索引（uq_lfgintent_char_boss，NULLS NOT DISTINCT）：
            // 過期那筆掛某王、未過期那筆掛任意王(NULL) → 不同組。
            await Dapper.SqlMapper.ExecuteAsync(conn, $"""INSERT INTO "LfgIntent"("DiscordId","CharacterId","BossId","ExpiresAt") VALUES (999,'c999',{bossId}, now() - interval '1 hour'), (999,'c999',NULL, now() + interval '1 hour');""");
        }

        var job = new Infrastructure.BackgroundJobs.LfgIntentCleanupJob(
            cs, Microsoft.Extensions.Logging.Abstractions.NullLogger<Infrastructure.BackgroundJobs.LfgIntentCleanupJob>.Instance);
        var deleted = await job.CleanupAsync(System.Threading.CancellationToken.None);

        Assert.Equal(1, deleted); // 只刪過期那筆
        await using var conn2 = new Npgsql.NpgsqlConnection(cs);
        await conn2.OpenAsync();
        var remaining = await Dapper.SqlMapper.QuerySingleAsync<int>(conn2, """SELECT COUNT(*)::int FROM "LfgIntent";""");
        Assert.Equal(1, remaining); // 未過期那筆留著
    }

    [Fact]
    public async Task PlayerAvailabilityOverride_Create_Get_Delete_RoundTrips_And_IsIdorSafe()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        await Seed.PlayerAsync(cs, 999, "P999");
        await Seed.PlayerAsync(cs, 888, "P888");
        var repo = new PlayerAvailabilityOverrideRepository(_fx.CreateDbContext());

        await repo.CreateAsync(new PlayerAvailabilityOverride
        {
            DiscordId = 999,
            Date = new DateOnly(2026, 4, 8),
            StartTime = new TimeOnly(19, 0),
            EndTime = new TimeOnly(22, 0),
            IsAvailable = false
        });

        var loaded = (await repo.GetByDiscordIdAsync(999)).ToList();
        Assert.Single(loaded);
        // DateOnly/TimeOnly 型別處理器 round-trip（寫 → 讀）
        Assert.Equal(new DateOnly(2026, 4, 8), loaded[0].Date);
        Assert.Equal(new TimeOnly(19, 0), loaded[0].StartTime);
        Assert.False(loaded[0].IsAvailable);

        // IDOR：別人（888）刪不掉 999 的 override
        Assert.Equal(0, await repo.DeleteAsync(888, loaded[0].Id));
        Assert.Single(await repo.GetByDiscordIdAsync(999));

        // 本人刪得掉
        Assert.Equal(1, await repo.DeleteAsync(999, loaded[0].Id));
        Assert.Empty(await repo.GetByDiscordIdAsync(999));
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

    [Fact]
    public async Task CharacterBossClear_Upsert_InsertsThenOverwritesSameCharBoss()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs);
        await Seed.PlayerAsync(cs, 556, "P");
        await Seed.CharacterAsync(cs, "c556", 556, "角色", "夜使者", 1200);

        var repo = new CharacterBossClearRepository(_fx.CreateDbContext());

        // 首次 upsert → 插入
        await repo.UpsertAsync(new CharacterBossClear { CharacterId = "c556", BossId = bossId, ClearCount = 3 });
        // 同角色同王再 upsert → 覆寫（uq_charbossclear 走 ON CONFLICT，不是新增第二筆）
        await repo.UpsertAsync(new CharacterBossClear { CharacterId = "c556", BossId = bossId, ClearCount = 8 });

        var loaded = (await new CharacterBossClearRepository(_fx.CreateDbContext()).GetByCharacterIdAsync("c556")).ToList();

        Assert.Single(loaded);            // 仍只有一筆
        Assert.Equal(8, loaded[0].ClearCount); // 值被覆寫成最新
    }
}
