using Application.DTOs;
using Dapper;
using Domain.Entities;
using Infrastructure.Query;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// leader-led Phase 1b 開隊寫路徑：TeamLeaderService.CreateTeamAsync 對真 Postgres 建 leader 隊 + 條件。
/// 驗新欄（Source=leader / PeriodId / LeaderDiscordId / Description）真的寫入，且條件列+職業一起落。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class TeamLeaderServiceIntegrationTests
{
    private readonly PostgresFixture _fx;
    public TeamLeaderServiceIntegrationTests(PostgresFixture fx) => _fx = fx;

    private TeamLeaderService CreateService()
    {
        var db = _fx.CreateDbContext();
        return new TeamLeaderService(
            new BossRepository(db),
            new PeriodQuery(db),
            new TeamSlotRepository(db),
            new TeamSlotRequirementRepository(db),
            new TeamCandidateQuery(db),
            new TeamSlotCharacterRepository(db),
            new CharacterQuery(db, new PeriodQuery(db)),
            new RegistrationLock(db),
            new Outbox(db),
            new TeamMembershipQuery(db));
    }

    [Fact]
    public async Task CreateTeamAsync_PersistsLeaderTeamWithRequirements()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs, requireMembers: 6);
        var periodId = await Seed.PeriodAsync(cs,
            new DateTimeOffset(2026, 4, 7, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 13, 23, 59, 59, TimeSpan.Zero));
        await Seed.PlayerAsync(cs, 999, "隊長");

        var slot = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var teamSlotId = await CreateService().CreateTeamAsync(new CreateTeamCommand
        {
            LeaderDiscordId = 999,
            BossId = bossId,
            SlotDateTime = slot,
            Description = "楓葉祝福9",
            Requirements =
            [
                new CreateTeamRequirementDto
                {
                    Count = 1, MinClearCount = 1,
                    Jobs =
                    [
                        new CreateTeamRequirementJobDto { Job = "箭神", MinAttackPower = 900 },
                        new CreateTeamRequirementJobDto { Job = "槍神", MinAttackPower = 1000 },
                    ]
                }
            ]
        });

        Assert.True(teamSlotId > 0);

        // TeamSlot 新欄實際寫入（read 路徑尚未映射這些欄，故直接查 DB 驗）
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var row = await conn.QuerySingleAsync<(string Source, int? PeriodId, long? LeaderDiscordId, string? Description)>(
            """SELECT "Source", "PeriodId", "LeaderDiscordId", "Description" FROM "TeamSlot" WHERE "Id" = @id;""",
            new { id = teamSlotId });
        Assert.Equal(TeamSlotSource.Leader, row.Source);
        Assert.Equal(periodId, row.PeriodId);
        Assert.Equal(999L, row.LeaderDiscordId);
        Assert.Equal("楓葉祝福9", row.Description);

        // 條件列 + 職業一起落
        var reqs = (await new TeamSlotRequirementRepository(_fx.CreateDbContext())
            .GetByTeamSlotIdAsync(teamSlotId)).ToList();
        Assert.Single(reqs);
        Assert.Equal(2, reqs[0].Jobs.Count);
        Assert.Contains(reqs[0].Jobs, j => j.Job == "箭神" && j.MinAttackPower == 900);
    }

    [Fact]
    public async Task GetCandidatesAsync_FiltersByTime_Job_Attack_And_ClearCount()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs, requireMembers: 6);
        var periodId = await Seed.PeriodAsync(cs,
            new DateTimeOffset(2026, 4, 7, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 13, 23, 59, 59, TimeSpan.Zero));

        // 團：週三 20:00 TPE（2026-04-08 12:00 UTC）；條件 箭神(≥900) or 槍神(≥1000) 1位、通關≥1
        var slot = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        await Seed.PlayerAsync(cs, 999, "隊長");
        var teamSlotId = await CreateService().CreateTeamAsync(new CreateTeamCommand
        {
            LeaderDiscordId = 999,
            BossId = bossId,
            SlotDateTime = slot,
            Requirements = [ new CreateTeamRequirementDto { Count = 1, MinClearCount = 1,
                Jobs = [ new CreateTeamRequirementJobDto { Job = "箭神", MinAttackPower = 900 },
                         new CreateTeamRequirementJobDto { Job = "槍神", MinAttackPower = 1000 } ] } ]
        });

        // 週三 = ISO weekday 3；時段 19:00-22:00 涵蓋 20:00
        const int wed = 3;
        // A：箭神 950、週三可、通關 2 → ✅ 唯一該中的
        await SeedCandidate(cs, periodId, bossId, 101, "archer", "箭神", 950, wed, clears: 2);
        // B：箭神 800（攻擊不足）→ ✗
        await SeedCandidate(cs, periodId, bossId, 102, "weak", "箭神", 800, wed, clears: 5);
        // C：主教（職業不符）→ ✗
        await SeedCandidate(cs, periodId, bossId, 103, "bishop", "主教", 1500, wed, clears: 5);
        // D：箭神 1000 但週四（時段不重疊）→ ✗
        await SeedCandidate(cs, periodId, bossId, 104, "archer2", "箭神", 1000, weekday: 4, clears: 5);
        // E：箭神 1000、週三可，但通關 0（＜門檻 1）→ ✗
        await SeedCandidate(cs, periodId, bossId, 105, "rookie", "箭神", 1000, wed, clears: 0);

        var candidates = (await CreateService().GetCandidatesAsync(teamSlotId)).ToList();

        Assert.Single(candidates);
        var c = candidates[0];
        Assert.Equal("archer", c.CharacterId);
        Assert.Equal("箭神", c.Job);
        Assert.Equal(950, c.AttackPower);
        Assert.Equal(2, c.BossClearCount);
    }

    [Fact]
    public async Task Invite_Then_Accept_ConfirmsMember()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs, requireMembers: 6);
        await Seed.PeriodAsync(cs, new DateTimeOffset(2026, 4, 7, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 13, 23, 59, 59, TimeSpan.Zero));
        await Seed.PlayerAsync(cs, 999, "隊長");
        await Seed.PlayerAsync(cs, 101, "P101");
        await Seed.CharacterAsync(cs, "archer", 101, "C", "箭神", 950);

        var slot = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var teamId = await CreateService().CreateTeamAsync(new CreateTeamCommand
        {
            LeaderDiscordId = 999,
            BossId = bossId,
            SlotDateTime = slot,
            Requirements = [ new CreateTeamRequirementDto { Count = 1,
                Jobs = [ new CreateTeamRequirementJobDto { Job = "箭神", MinAttackPower = 900 } ] } ]
        });

        await CreateService().InviteMemberAsync(teamId, "archer", leaderDiscordId: 999);
        var memberId = await GetMemberIdAsync(cs, teamId, "archer");

        await CreateService().AcceptInviteAsync(memberId, currentDiscordId: 101);

        Assert.Equal(1, await new TeamSlotCharacterRepository(_fx.CreateDbContext()).CountConfirmedAsync(teamId));
        Assert.Equal("Confirmed", await StatusOfAsync(cs, memberId));
    }

    [Fact]
    public async Task Accept_SecondOverlappingTeam_ViolatesCrossTeamUnique()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs, requireMembers: 6);
        await Seed.PeriodAsync(cs, new DateTimeOffset(2026, 4, 7, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 13, 23, 59, 59, TimeSpan.Zero));
        await Seed.PlayerAsync(cs, 999, "隊長");
        await Seed.PlayerAsync(cs, 101, "P101");
        await Seed.CharacterAsync(cs, "archer", 101, "C", "箭神", 950);

        // 兩隊同一時段（跨隊重疊）
        var slot = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        CreateTeamCommand Cmd() => new()
        {
            LeaderDiscordId = 999,
            BossId = bossId,
            SlotDateTime = slot,
            Requirements = [ new CreateTeamRequirementDto { Count = 1,
                Jobs = [ new CreateTeamRequirementJobDto { Job = "箭神", MinAttackPower = 900 } ] } ]
        };
        var teamA = await CreateService().CreateTeamAsync(Cmd());
        var teamB = await CreateService().CreateTeamAsync(Cmd());

        await CreateService().InviteMemberAsync(teamA, "archer", 999);
        await CreateService().InviteMemberAsync(teamB, "archer", 999);
        var mA = await GetMemberIdAsync(cs, teamA, "archer");
        var mB = await GetMemberIdAsync(cs, teamB, "archer");

        await CreateService().AcceptInviteAsync(mA, 101);   // A → Confirmed
        // 接受 B（同玩家同時段）→ uq_tsc_confirmed_overlap 觸發 23505（middleware 會轉 409）
        var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => CreateService().AcceptInviteAsync(mB, 101));
        Assert.Equal("23505", ex.SqlState);
    }

    [Fact]
    public async Task DuplicateInvite_SameCharacterSameTeam_ViolatesActiveMembershipUnique()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs, requireMembers: 6);
        await Seed.PeriodAsync(cs, new DateTimeOffset(2026, 4, 7, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 13, 23, 59, 59, TimeSpan.Zero));
        await Seed.PlayerAsync(cs, 999, "隊長");
        await Seed.PlayerAsync(cs, 101, "P101");
        await Seed.CharacterAsync(cs, "archer", 101, "C", "箭神", 950);

        var slot = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var teamId = await CreateService().CreateTeamAsync(new CreateTeamCommand
        {
            LeaderDiscordId = 999,
            BossId = bossId,
            SlotDateTime = slot,
            Requirements = [ new CreateTeamRequirementDto { Count = 1,
                Jobs = [ new CreateTeamRequirementJobDto { Job = "箭神", MinAttackPower = 900 } ] } ]
        });

        await CreateService().InviteMemberAsync(teamId, "archer", 999);
        var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(
            () => CreateService().InviteMemberAsync(teamId, "archer", 999));
        Assert.Equal("23505", ex.SqlState);
    }

    [Fact]
    public async Task Apply_Then_Approve_ConfirmsMember()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs, requireMembers: 6);
        await Seed.PeriodAsync(cs, new DateTimeOffset(2026, 4, 7, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 13, 23, 59, 59, TimeSpan.Zero));
        await Seed.PlayerAsync(cs, 999, "隊長");
        await Seed.PlayerAsync(cs, 101, "P101");
        await Seed.CharacterAsync(cs, "archer", 101, "C", "箭神", 950);

        var slot = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var teamId = await CreateService().CreateTeamAsync(new CreateTeamCommand
        {
            LeaderDiscordId = 999,
            BossId = bossId,
            SlotDateTime = slot,
            Requirements = [new CreateTeamRequirementDto { Count = 1, Jobs = [new CreateTeamRequirementJobDto { Job = "箭神", MinAttackPower = 900 }] }]
        });

        // 玩家申請（用本人角色）→ Applied
        await CreateService().ApplyAsync(teamId, "archer", applicantDiscordId: 101);
        var memberId = await GetMemberIdAsync(cs, teamId, "archer");
        Assert.Equal("Applied", await StatusOfAsync(cs, memberId));

        // 隊長核准 → Confirmed
        await CreateService().ApproveAsync(memberId, leaderDiscordId: 999);
        Assert.Equal("Confirmed", await StatusOfAsync(cs, memberId));
        Assert.Equal(1, await new TeamSlotCharacterRepository(_fx.CreateDbContext()).CountConfirmedAsync(teamId));
    }

    [Fact]
    public async Task Apply_Then_Reject_SetsRejected_And_NonLeaderCannotApprove()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs, requireMembers: 6);
        await Seed.PeriodAsync(cs, new DateTimeOffset(2026, 4, 7, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 13, 23, 59, 59, TimeSpan.Zero));
        await Seed.PlayerAsync(cs, 999, "隊長");
        await Seed.PlayerAsync(cs, 101, "P101");
        await Seed.CharacterAsync(cs, "archer", 101, "C", "箭神", 950);

        var slot = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var teamId = await CreateService().CreateTeamAsync(new CreateTeamCommand
        {
            LeaderDiscordId = 999,
            BossId = bossId,
            SlotDateTime = slot,
            Requirements = [new CreateTeamRequirementDto { Count = 1, Jobs = [new CreateTeamRequirementJobDto { Job = "箭神", MinAttackPower = 900 }] }]
        });

        await CreateService().ApplyAsync(teamId, "archer", 101);
        var memberId = await GetMemberIdAsync(cs, teamId, "archer");

        // 非隊長（別人）不能核准
        await Assert.ThrowsAsync<Application.Exceptions.ForbiddenException>(
            () => CreateService().ApproveAsync(memberId, leaderDiscordId: 888));

        // 隊長拒絕 → Rejected
        await CreateService().RejectAsync(memberId, leaderDiscordId: 999);
        Assert.Equal("Rejected", await StatusOfAsync(cs, memberId));
    }

    [Fact]
    public async Task Invite_EnqueuesTeamNotification_ForInvitedPlayer()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs, name: "西格諾斯", requireMembers: 6);
        await Seed.PeriodAsync(cs, new DateTimeOffset(2026, 4, 7, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 13, 23, 59, 59, TimeSpan.Zero));
        await Seed.PlayerAsync(cs, 999, "隊長");
        await Seed.PlayerAsync(cs, 101, "P101");
        await Seed.CharacterAsync(cs, "archer", 101, "C", "箭神", 950);

        var slot = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var teamId = await CreateService().CreateTeamAsync(new CreateTeamCommand
        {
            LeaderDiscordId = 999,
            BossId = bossId,
            SlotDateTime = slot,
            Requirements = [new CreateTeamRequirementDto { Count = 1, Jobs = [new CreateTeamRequirementJobDto { Job = "箭神", MinAttackPower = 900 }] }]
        });

        await CreateService().InviteMemberAsync(teamId, "archer", leaderDiscordId: 999);

        // 邀請的同交易內寫了一則 TeamNotification outbox，收件人＝被邀玩家 101（原子；bot 之後撈去發 DM）
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var row = await conn.QuerySingleAsync<(string Type, string Payload)>(
            """SELECT "Type", "Payload"::text FROM "OutboxMessage" ORDER BY "Id" DESC LIMIT 1;""");
        Assert.Equal("TeamNotification", row.Type);
        // jsonb::text 會正規化（key 排序/空白）→ 反序列化回物件再斷言，較穩健
        var evt = System.Text.Json.JsonSerializer.Deserialize<Application.Events.TeamNotificationEvent>(row.Payload)!;
        Assert.Equal(101UL, evt.TargetDiscordId);   // 收件人＝被邀玩家
        Assert.Contains("西格諾斯", evt.Message);    // 訊息帶王名
    }

    [Fact]
    public async Task ReadApis_MyInvitations_OpenTeams_Applications()
    {
        await _fx.ResetAsync();
        var cs = _fx.ConnectionString;
        var bossId = await Seed.BossAsync(cs, requireMembers: 6);
        var periodId = await Seed.PeriodAsync(cs, new DateTimeOffset(2026, 4, 7, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 13, 23, 59, 59, TimeSpan.Zero));
        await Seed.PlayerAsync(cs, 999, "隊長");
        await Seed.PlayerAsync(cs, 101, "P101");
        await Seed.CharacterAsync(cs, "archer", 101, "C1", "箭神", 950);
        await Seed.PlayerAsync(cs, 102, "P102");
        await Seed.CharacterAsync(cs, "mage", 102, "C2", "主教", 800);

        var slot = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        var teamId = await CreateService().CreateTeamAsync(new CreateTeamCommand
        {
            LeaderDiscordId = 999,
            BossId = bossId,
            SlotDateTime = slot,
            Requirements = [new CreateTeamRequirementDto { Count = 1, MinClearCount = 0, Jobs = [new CreateTeamRequirementJobDto { Job = "箭神", MinAttackPower = 900 }] }]
        });

        await CreateService().InviteMemberAsync(teamId, "archer", leaderDiscordId: 999);  // 101 被邀
        await CreateService().ApplyAsync(teamId, "mage", applicantDiscordId: 102);          // 102 申請

        var q = new TeamMembershipQuery(_fx.CreateDbContext());

        // 我的邀請（101）
        var invs = (await q.GetByDiscordIdAndStatusAsync(101, "Invited")).ToList();
        Assert.Single(invs);
        Assert.Equal(teamId, invs[0].TeamSlotId);
        Assert.Equal("箭神", invs[0].Job);

        // 申請佇列（隊長看）
        var apps = (await q.GetApplicationsAsync(teamId)).ToList();
        Assert.Single(apps);
        Assert.Equal("主教", apps[0].Job);

        // 開放隊（尚有空位）+ 條件
        var open = (await q.GetOpenTeamsAsync(periodId)).ToList();
        Assert.Single(open);
        Assert.Equal(teamId, open[0].TeamSlotId);
        Assert.Equal(0, open[0].ConfirmedCount);
        Assert.Equal(6, open[0].RequireMembers);
        Assert.Single(open[0].Requirements);
        Assert.Contains(open[0].Requirements[0].Jobs, j => j.Job == "箭神" && j.MinAttackPower == 900);
    }

    private static async Task<int> GetMemberIdAsync(string cs, int teamSlotId, string charId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<int>(
            """SELECT "Id" FROM "TeamSlotCharacter" WHERE "TeamSlotId"=@teamSlotId AND "CharacterId"=@charId;""",
            new { teamSlotId, charId });
    }

    private static async Task<string> StatusOfAsync(string cs, int memberId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<string>(
            """SELECT "Status" FROM "TeamSlotCharacter" WHERE "Id"=@memberId;""", new { memberId });
    }

    private async Task SeedCandidate(string cs, int periodId, int bossId, long discordId, string charId,
        string job, int atk, int weekday, int clears)
    {
        await Seed.PlayerAsync(cs, discordId, $"P{discordId}");
        await Seed.CharacterAsync(cs, charId, discordId, $"C{charId}", job, atk);
        var prId = await Seed.PlayerRegisterAsync(cs, discordId, periodId);
        await Seed.CharacterRegisterAsync(cs, prId, charId, bossId, rounds: 1);
        await Seed.AvailabilityAsync(cs, prId, weekday, new TimeOnly(19, 0), new TimeOnly(22, 0));
        if (clears > 0)
        {
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                """INSERT INTO "CharacterBossClear"("CharacterId","BossId","ClearCount") VALUES (@charId,@bossId,@clears);""",
                new { charId, bossId, clears });
        }
    }
}
