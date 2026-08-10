using Application.DTOs;
using Application.Queries;
using Infrastructure.Dapper;

namespace Infrastructure.Query;

public class TeamMembershipQuery : ITeamMembershipQuery
{
    private readonly DbContext _dbContext;

    public TeamMembershipQuery(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<MembershipDto>> GetByDiscordIdAndStatusAsync(ulong discordId, string status)
    {
        const string sql = """
            SELECT tsc."Id" AS "MemberId", tsc."TeamSlotId" AS "TeamSlotId", b."Name" AS "BossName",
                   ts."SlotDateTime" AS "SlotDateTime", tsc."CharacterId" AS "CharacterId",
                   tsc."CharacterName" AS "CharacterName", tsc."Job" AS "Job",
                   tsc."AttackPower" AS "AttackPower", tsc."Status" AS "Status",
                   b."RequireMembers" AS "RequireMembers",
                   (SELECT COUNT(*) FROM "TeamSlotCharacter" c
                    WHERE c."TeamSlotId" = ts."Id" AND c."Status" = 'Confirmed')::int AS "ConfirmedCount"
            FROM "TeamSlotCharacter" tsc
            JOIN "TeamSlot" ts ON ts."Id" = tsc."TeamSlotId"
            JOIN "Boss" b      ON b."Id" = ts."BossId"
            WHERE tsc."DiscordId" = @discordId AND tsc."Status" = @status
            ORDER BY ts."SlotDateTime";
            """;
        return await _dbContext.QueryAsync<MembershipDto>(sql, new { discordId = (long)discordId, status });
    }

    public async Task<IEnumerable<MembershipDto>> GetApplicationsAsync(int teamSlotId)
    {
        const string sql = """
            SELECT tsc."Id" AS "MemberId", tsc."TeamSlotId" AS "TeamSlotId", b."Name" AS "BossName",
                   ts."SlotDateTime" AS "SlotDateTime", tsc."CharacterId" AS "CharacterId",
                   tsc."CharacterName" AS "CharacterName", tsc."Job" AS "Job",
                   tsc."AttackPower" AS "AttackPower", tsc."Status" AS "Status",
                   b."RequireMembers" AS "RequireMembers",
                   (SELECT COUNT(*) FROM "TeamSlotCharacter" c
                    WHERE c."TeamSlotId" = ts."Id" AND c."Status" = 'Confirmed')::int AS "ConfirmedCount"
            FROM "TeamSlotCharacter" tsc
            JOIN "TeamSlot" ts ON ts."Id" = tsc."TeamSlotId"
            JOIN "Boss" b      ON b."Id" = ts."BossId"
            WHERE tsc."TeamSlotId" = @teamSlotId AND tsc."Status" = 'Applied'
            ORDER BY tsc."Id";
            """;
        return await _dbContext.QueryAsync<MembershipDto>(sql, new { teamSlotId });
    }

    public async Task<IEnumerable<OpenTeamDto>> GetOpenTeamsAsync(int periodId)
    {
        // leader 開放隊：本期、Confirmed 真實成員 < RequireMembers
        const string teamsSql = """
            SELECT ts."Id" AS "TeamSlotId", ts."BossId" AS "BossId", b."Name" AS "BossName",
                   ts."SlotDateTime" AS "SlotDateTime", b."RequireMembers" AS "RequireMembers",
                   ts."Description" AS "Description",
                   (SELECT COUNT(*) FROM "TeamSlotCharacter" c
                    WHERE c."TeamSlotId" = ts."Id" AND c."Status" = 'Confirmed' AND c."DiscordId" <> 0)::int AS "ConfirmedCount"
            FROM "TeamSlot" ts
            JOIN "Boss" b ON b."Id" = ts."BossId"
            WHERE ts."Source" = 'leader' AND ts."PeriodId" = @periodId
            ORDER BY ts."SlotDateTime";
            """;
        var teams = (await _dbContext.QueryAsync<OpenTeamDto>(teamsSql, new { periodId }))
            .Where(t => t.ConfirmedCount < t.RequireMembers)   // 只回尚有空位的
            .ToList();
        if (teams.Count == 0) return teams;

        // 一次撈這些隊的所有需求列 + 職業（避免 N+1）
        var ids = teams.Select(t => t.TeamSlotId).ToArray();
        const string reqSql = """
            SELECT r."Id" AS "RequirementId", r."TeamSlotId" AS "TeamSlotId", r."Count" AS "Count",
                   r."MinClearCount" AS "MinClearCount", j."Job" AS "Job", j."MinAttackPower" AS "MinAttackPower"
            FROM "TeamSlotRequirement" r
            LEFT JOIN "TeamSlotRequirementJob" j ON j."RequirementId" = r."Id"
            WHERE r."TeamSlotId" = ANY(@ids);
            """;
        var reqRows = (await _dbContext.QueryAsync<ReqRow>(reqSql, new { ids })).ToList();

        var byTeam = reqRows.GroupBy(r => r.TeamSlotId).ToDictionary(g => g.Key, g =>
            g.GroupBy(r => r.RequirementId).Select(rg =>
            {
                var f = rg.First();
                return new OpenTeamRequirementDto
                {
                    Count = f.Count,
                    MinClearCount = f.MinClearCount,
                    Jobs = rg.Where(x => x.Job != null)
                        .Select(x => new OpenTeamRequirementJobDto { Job = x.Job!, MinAttackPower = x.MinAttackPower })
                        .ToList()
                };
            }).ToList());

        foreach (var t in teams)
            t.Requirements = byTeam.GetValueOrDefault(t.TeamSlotId, []);

        return teams;
    }

    public async Task<IEnumerable<LedTeamDto>> GetLedTeamsAsync(ulong leaderDiscordId, int periodId)
    {
        // 本期、LeaderDiscordId=本人 的隊；LEFT JOIN 成員一次算三種狀態計數（避免 N+1）。
        const string sql = """
            SELECT ts."Id" AS "TeamSlotId", ts."BossId" AS "BossId", b."Name" AS "BossName",
                   ts."SlotDateTime" AS "SlotDateTime", b."RequireMembers" AS "RequireMembers",
                   ts."Description" AS "Description",
                   COUNT(*) FILTER (WHERE tsc."Status" = 'Confirmed')::int AS "ConfirmedCount",
                   COUNT(*) FILTER (WHERE tsc."Status" = 'Applied')::int   AS "AppliedCount",
                   COUNT(*) FILTER (WHERE tsc."Status" = 'Invited')::int   AS "InvitedCount"
            FROM "TeamSlot" ts
            JOIN "Boss" b ON b."Id" = ts."BossId"
            LEFT JOIN "TeamSlotCharacter" tsc ON tsc."TeamSlotId" = ts."Id"
            WHERE ts."LeaderDiscordId" = @leaderDiscordId AND ts."PeriodId" = @periodId
            GROUP BY ts."Id", ts."BossId", b."Name", ts."SlotDateTime", b."RequireMembers", ts."Description"
            ORDER BY ts."SlotDateTime";
            """;
        return await _dbContext.QueryAsync<LedTeamDto>(sql, new { leaderDiscordId = (long)leaderDiscordId, periodId });
    }

    public async Task<IEnumerable<LeaderTransferDto>> GetPendingLeaderTransfersAsync(ulong discordId)
    {
        const string sql = """
            SELECT ts."Id" AS "TeamSlotId", b."Name" AS "BossName", ts."SlotDateTime" AS "SlotDateTime"
            FROM "TeamSlot" ts
            JOIN "Boss" b ON b."Id" = ts."BossId"
            WHERE ts."PendingLeaderDiscordId" = @discordId
            ORDER BY ts."SlotDateTime";
            """;
        return await _dbContext.QueryAsync<LeaderTransferDto>(sql, new { discordId = (long)discordId });
    }

    public async Task<IEnumerable<RosterMemberDto>> GetConfirmedRosterAsync(int teamSlotId)
    {
        const string sql = """
            SELECT tsc."Id" AS "MemberId", tsc."CharacterName" AS "CharacterName", p."DiscordName" AS "DiscordName"
            FROM "TeamSlotCharacter" tsc
            JOIN "Player" p ON p."DiscordId" = tsc."DiscordId"
            WHERE tsc."TeamSlotId" = @teamSlotId AND tsc."Status" = 'Confirmed'
            ORDER BY tsc."Id";
            """;
        return await _dbContext.QueryAsync<RosterMemberDto>(sql, new { teamSlotId });
    }

    private class ReqRow
    {
        public int RequirementId { get; set; }
        public int TeamSlotId { get; set; }
        public int Count { get; set; }
        public int MinClearCount { get; set; }
        public string? Job { get; set; }
        public int MinAttackPower { get; set; }
    }
}
