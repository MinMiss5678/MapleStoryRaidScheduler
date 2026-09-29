using System.Text.Json;
using Application.Events;
using Application.Interface;
using Dapper;
using Infrastructure.BackgroundJobs;
using Infrastructure.Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 有按鈕的通知送出前檢查是否仍有效（plans/2026-09-22-outbox-retry-backoff.md E1–E6）：
/// 延遲送達時邀請可能已撤／已滿、申請已處理、轉讓已取消 → 不送，避免留下按不了的死按鈕。
/// 失效 → 丟 <see cref="OutboxDeliverySkippedException"/>（dispatcher 標完成、記原因、不警示）。
/// </summary>
[Collection("pg")]
public class TeamNotificationStalenessIntegrationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fx;
    public TeamNotificationStalenessIntegrationTests(PostgresFixture fx) => _fx = fx;

    public Task InitializeAsync() => _fx.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private readonly FakeDiscord _discord = new();
    private readonly HashSet<long> _seededPlayers = new();

    private TeamNotificationOutboxHandler Handler() =>
        new(_discord, new NpgsqlConnectionFactory(_fx.ConnectionString), NullLogger<TeamNotificationOutboxHandler>.Instance);

    private async Task<int> TeamAsync(long? pendingLeader = null)
    {
        // PendingLeaderDiscordId 有 FK 到 Player → 先建玩家
        if (pendingLeader is { } p && !_seededPlayers.Contains(p))
        {
            await Seed.PlayerAsync(_fx.ConnectionString, p, $"P{p}");
            _seededPlayers.Add(p);
        }
        var bossId = await Seed.BossAsync(_fx.ConnectionString, "王", 6);
        var teamId = await Seed.TeamSlotAsync(_fx.ConnectionString, bossId, "leader");
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.ExecuteAsync("""UPDATE "TeamSlot" SET "PendingLeaderDiscordId" = @p WHERE "Id" = @id""",
            new { p = pendingLeader, id = teamId });
        return teamId;
    }

    private async Task<int> MemberAsync(int teamId, string status)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        return await conn.ExecuteScalarAsync<int>(
            """INSERT INTO "TeamSlotCharacter"("TeamSlotId","DiscordId","Job","Status") VALUES (@teamId,701,'英雄',@status) RETURNING "Id";""",
            new { teamId, status });
    }

    private static string Payload(TeamNotificationAction action, int? actionId, ulong target = 701) =>
        JsonSerializer.Serialize(new TeamNotificationEvent { TargetDiscordId = target, Message = "m", Action = action, ActionId = actionId });

    [Fact]
    public async Task E1_邀請_成員仍為Invited_送出()
    {
        var memberId = await MemberAsync(await TeamAsync(), "Invited");

        await Handler().HandleAsync(Payload(TeamNotificationAction.InviteResponse, memberId), CancellationToken.None);

        Assert.Equal(1, _discord.Sent);
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Confirmed")]
    [InlineData("Left")]
    public async Task E2_邀請_成員已非Invited_判定失效不送(string status)
    {
        var memberId = await MemberAsync(await TeamAsync(), status);

        await Assert.ThrowsAsync<OutboxDeliverySkippedException>(() =>
            Handler().HandleAsync(Payload(TeamNotificationAction.InviteResponse, memberId), CancellationToken.None));
        Assert.Equal(0, _discord.Sent);
    }

    [Fact]
    public async Task E3_邀請_成員資料不存在_判定失效不送()
    {
        await Assert.ThrowsAsync<OutboxDeliverySkippedException>(() =>
            Handler().HandleAsync(Payload(TeamNotificationAction.InviteResponse, 999999), CancellationToken.None));
        Assert.Equal(0, _discord.Sent);
    }

    [Fact]
    public async Task E4_申請_仍為Applied送出_否則判定失效()
    {
        var teamId = await TeamAsync();
        var applied = await MemberAsync(teamId, "Applied");
        var rejected = await MemberAsync(teamId, "Rejected");

        await Handler().HandleAsync(Payload(TeamNotificationAction.ApplicationReview, applied), CancellationToken.None);
        Assert.Equal(1, _discord.Sent);

        await Assert.ThrowsAsync<OutboxDeliverySkippedException>(() =>
            Handler().HandleAsync(Payload(TeamNotificationAction.ApplicationReview, rejected), CancellationToken.None));
        Assert.Equal(1, _discord.Sent);
    }

    [Fact]
    public async Task E5_轉讓_PendingLeader為收件人送出_否則判定失效()
    {
        var pendingForTarget = await TeamAsync(pendingLeader: 701);
        var pendingForOther = await TeamAsync(pendingLeader: 888);
        var noPending = await TeamAsync(pendingLeader: null);

        await Handler().HandleAsync(Payload(TeamNotificationAction.TransferResponse, pendingForTarget), CancellationToken.None);
        Assert.Equal(1, _discord.Sent);

        foreach (var teamId in new[] { pendingForOther, noPending })
            await Assert.ThrowsAsync<OutboxDeliverySkippedException>(() =>
                Handler().HandleAsync(Payload(TeamNotificationAction.TransferResponse, teamId), CancellationToken.None));
        Assert.Equal(1, _discord.Sent);
    }

    [Fact]
    public async Task E6_純文字與撤邀清理_不查DB_行為不變()
    {
        await Handler().HandleAsync(Payload(TeamNotificationAction.None, null), CancellationToken.None);
        Assert.Equal(1, _discord.Sent);

        var cleanup = JsonSerializer.Serialize(new TeamNotificationEvent
        {
            TargetDiscordId = 701,
            Message = "已失效",
            Action = TeamNotificationAction.InviteRevokedCleanup,
            EditMessageId = 123
        });
        await Handler().HandleAsync(cleanup, CancellationToken.None);
        Assert.Equal(1, _discord.Edited);
    }

    private sealed class FakeDiscord : IDiscordService
    {
        public int Sent { get; private set; }
        public int Edited { get; private set; }

        public Task SendDirectMessageAsync(ulong discordId, string message) { Sent++; return Task.CompletedTask; }
        public Task<ulong> SendDirectMessageAsync(ulong discordId, string message, IReadOnlyList<DmButton> buttons) { Sent++; return Task.FromResult(1UL); }
        public Task<ulong> SendDirectMessageAsync(ulong discordId, DmEmbed embed, IReadOnlyList<DmButton> buttons) { Sent++; return Task.FromResult(1UL); }
        public Task EditDirectMessageAsync(ulong discordId, ulong messageId, string content) { Edited++; return Task.CompletedTask; }
    }
}
