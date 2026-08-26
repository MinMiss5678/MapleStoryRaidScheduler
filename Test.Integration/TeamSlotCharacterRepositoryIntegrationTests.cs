using Dapper;
using Domain.Entities;
using Infrastructure.Repositories;
using Npgsql;
using Xunit;

namespace Test.Integration;

[Collection("pg")]
[Trait("Category", "Integration")]
public class TeamSlotCharacterRepositoryIntegrationTests
{
    private readonly PostgresFixture _fx;
    public TeamSlotCharacterRepositoryIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task DeleteCharacter_LastMember_AutoTeam_AutoDeletesEmptyTeam()
    {
        await _fx.ResetAsync();
        var bossId = await Seed.BossAsync(_fx.ConnectionString);
        var teamId = await Seed.TeamSlotAsync(_fx.ConnectionString, bossId, TeamSlotSource.Auto);
        var charId = await Seed.OccupiedSlotAsync(_fx.ConnectionString, teamId);

        var repo = new TeamSlotCharacterRepository(_fx.CreateDbContext());
        await repo.DeleteCharacterAsync(new TeamSlotCharacter { Id = charId, TeamSlotId = teamId, DiscordName = "", Job = "" });

        // auto 隊移除最後成員 → 空隊自動清除
        Assert.Equal(0, await Seed.CountTeamAsync(_fx.ConnectionString, teamId));
    }

    [Fact]
    public async Task DeleteCharacter_LastMember_AdminTeam_KeepsEmptyTeam()
    {
        await _fx.ResetAsync();
        var bossId = await Seed.BossAsync(_fx.ConnectionString);
        var teamId = await Seed.TeamSlotAsync(_fx.ConnectionString, bossId, TeamSlotSource.Admin);
        var charId = await Seed.OccupiedSlotAsync(_fx.ConnectionString, teamId);

        var repo = new TeamSlotCharacterRepository(_fx.CreateDbContext());
        await repo.DeleteCharacterAsync(new TeamSlotCharacter { Id = charId, TeamSlotId = teamId, DiscordName = "", Job = "" });

        // admin 手動開的隊即使變空也保留
        Assert.Equal(1, await Seed.CountTeamAsync(_fx.ConnectionString, teamId));
    }

    [Fact]
    public async Task RevokePendingInvites_ReturnsDiscordIdAndDmMessageId_AndMarksRejected()
    {
        // dm-revoke-cleanup：撤邀回傳 (DiscordId, DmMessageId)；未回寫 id 者為 null。驗 RETURNING DmMessageId + migration 000024。
        await _fx.ResetAsync();
        var bossId = await Seed.BossAsync(_fx.ConnectionString);
        var teamId = await Seed.TeamSlotAsync(_fx.ConnectionString, bossId, TeamSlotSource.Leader);
        await Seed.PlayerAsync(_fx.ConnectionString, 701, "A");
        await Seed.PlayerAsync(_fx.ConnectionString, 702, "B");

        await using (var c = new NpgsqlConnection(_fx.ConnectionString))
        {
            await c.OpenAsync();
            // 一個已回寫 DM message id、一個未回寫（null）
            await c.ExecuteAsync(
                """INSERT INTO "TeamSlotCharacter"("TeamSlotId","DiscordId","Job","Status","DmMessageId") VALUES (@t,701,'英雄','Invited',999888)""",
                new { t = teamId });
            await c.ExecuteAsync(
                """INSERT INTO "TeamSlotCharacter"("TeamSlotId","DiscordId","Job","Status") VALUES (@t,702,'夜使者','Invited')""",
                new { t = teamId });
        }

        var repo = new TeamSlotCharacterRepository(_fx.CreateDbContext());
        var revoked = (await repo.RevokePendingInvitesAsync(teamId)).ToList();

        Assert.Equal(2, revoked.Count);
        Assert.Equal(999888UL, revoked.Single(r => r.DiscordId == 701).DmMessageId);
        Assert.Null(revoked.Single(r => r.DiscordId == 702).DmMessageId);

        // 皆轉 Rejected（不再是 Invited）
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        var stillInvited = await conn.ExecuteScalarAsync<int>(
            """SELECT COUNT(*) FROM "TeamSlotCharacter" WHERE "TeamSlotId" = @t AND "Status" = 'Invited'""", new { t = teamId });
        Assert.Equal(0, stillInvited);
    }

    [Fact]
    public async Task GetConfirmedBookingsInRange_OnlyConfirmedWithinRange()
    {
        // 招募熱力圖：只回範圍內 Confirmed 訂位（範圍外/非 Confirmed 不回）。
        await _fx.ResetAsync();
        var bossId = await Seed.BossAsync(_fx.ConnectionString);
        var teamId = await Seed.TeamSlotAsync(_fx.ConnectionString, bossId, TeamSlotSource.Leader);
        await Seed.PlayerAsync(_fx.ConnectionString, 501, "A");
        await Seed.PlayerAsync(_fx.ConnectionString, 502, "B");
        await Seed.PlayerAsync(_fx.ConnectionString, 503, "C");
        var inRange = new DateTimeOffset(2026, 9, 1, 20, 0, 0, TimeSpan.Zero);
        var outRange = new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.Zero);

        await using (var c = new NpgsqlConnection(_fx.ConnectionString))
        {
            await c.OpenAsync();
            await c.ExecuteAsync("""INSERT INTO "TeamSlotCharacter"("TeamSlotId","DiscordId","Job","Status","SlotDateTime") VALUES (@t,501,'英雄','Confirmed',@s)""", new { t = teamId, s = inRange });
            await c.ExecuteAsync("""INSERT INTO "TeamSlotCharacter"("TeamSlotId","DiscordId","Job","Status","SlotDateTime") VALUES (@t,502,'英雄','Confirmed',@s)""", new { t = teamId, s = outRange });     // 範圍外
            await c.ExecuteAsync("""INSERT INTO "TeamSlotCharacter"("TeamSlotId","DiscordId","Job","Status","SlotDateTime") VALUES (@t,503,'英雄','Invited',@s)""", new { t = teamId, s = inRange });        // 非 Confirmed
        }

        var repo = new TeamSlotCharacterRepository(_fx.CreateDbContext());
        var bookings = (await repo.GetConfirmedBookingsInRangeAsync(
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero))).ToList();

        Assert.Single(bookings);
        Assert.Equal(501UL, bookings[0].DiscordId);
        Assert.Equal(inRange.ToUniversalTime(), bookings[0].SlotDateTime.ToUniversalTime());
    }
}
