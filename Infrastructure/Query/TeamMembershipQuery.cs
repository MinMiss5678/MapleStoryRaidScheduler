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
                   tsc."CharacterName" AS "CharacterName", p."DiscordName" AS "DiscordName", tsc."Job" AS "Job",
                   tsc."AttackPower" AS "AttackPower", tsc."Status" AS "Status",
                   b."RequireMembers" AS "RequireMembers",
                   COALESCE(ch."MapleBlessingLevel", 0) AS "MapleBlessingLevel",
                   COALESCE((SELECT SUM(cbc."ClearCount") FROM "CharacterBossClear" cbc
                             JOIN "Character" c2 ON c2."Id" = cbc."CharacterId"
                             WHERE c2."DiscordId" = tsc."DiscordId" AND cbc."BossId" = ts."BossId"), 0)::int AS "BossClearCount",
                   (SELECT COUNT(*) FROM "TeamSlotCharacter" c
                    WHERE c."TeamSlotId" = ts."Id" AND c."Status" = 'Confirmed')::int AS "ConfirmedCount"
            FROM "TeamSlotCharacter" tsc
            JOIN "TeamSlot" ts ON ts."Id" = tsc."TeamSlotId"
            JOIN "Boss" b      ON b."Id" = ts."BossId"
            JOIN "Player" p    ON p."DiscordId" = tsc."DiscordId"
            LEFT JOIN "Character" ch ON ch."Id" = tsc."CharacterId"
            WHERE tsc."TeamSlotId" = @teamSlotId AND tsc."Status" = 'Applied'
            ORDER BY tsc."Id";
            """;
        return await _dbContext.QueryAsync<MembershipDto>(sql, new { teamSlotId });
    }

    public async Task<IEnumerable<OpenTeamDto>> GetOpenTeamsAsync(ulong currentDiscordId)
    {
        // leader 開放隊：Confirmed 真實成員 < RequireMembers。period-less §8 Phase 4a：時間窗取代 period——
        // 未來/近期排程(SlotDateTime > now()-1天) + 未過期即時。排除呼叫者自己開的隊（不會申請自己的隊）。
        const string teamsSql = """
            SELECT ts."Id" AS "TeamSlotId", ts."BossId" AS "BossId", b."Name" AS "BossName",
                   ts."SlotDateTime" AS "SlotDateTime", b."RequireMembers" AS "RequireMembers",
                   ts."Description" AS "Description",
                   (SELECT COUNT(*) FROM "TeamSlotCharacter" c
                    WHERE c."TeamSlotId" = ts."Id" AND c."Status" = 'Confirmed' AND c."DiscordId" <> 0)::int AS "ConfirmedCount"
            FROM "TeamSlot" ts
            JOIN "Boss" b ON b."Id" = ts."BossId"
            WHERE ts."Source" = 'leader'
              AND ts."LeaderDiscordId" IS DISTINCT FROM @currentDiscordId
              -- 排除呼叫者已在裡面的隊（Confirmed/Invited/Applied）——尋隊只列「還沒加入的隊」。
              AND NOT EXISTS (
                  SELECT 1 FROM "TeamSlotCharacter" m
                  WHERE m."TeamSlotId" = ts."Id" AND m."DiscordId" = @currentDiscordId
                    AND m."Status" IN ('Confirmed', 'Invited', 'Applied'))
              AND ( (ts."Kind" = 'Scheduled' AND ts."SlotDateTime" > now() - interval '1 day')
                 OR (ts."Kind" = 'Instant'   AND ts."ExpiresAt"   > now()) )
            ORDER BY ts."SlotDateTime";
            """;
        var teams = (await _dbContext.QueryAsync<OpenTeamDto>(teamsSql, new { currentDiscordId = (long)currentDiscordId }))
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

        // 一次撈這些隊的已確認成員能力（職業/攻擊快照 + 祝福 join Character；不含身分——尋隊公開面 §9.12）
        const string memSql = """
            SELECT tsc."TeamSlotId" AS "TeamSlotId", tsc."Job" AS "Job", tsc."AttackPower" AS "AttackPower",
                   COALESCE(ch."MapleBlessingLevel", 0) AS "MapleBlessingLevel"
            FROM "TeamSlotCharacter" tsc
            LEFT JOIN "Character" ch ON ch."Id" = tsc."CharacterId"
            WHERE tsc."TeamSlotId" = ANY(@ids) AND tsc."Status" = 'Confirmed'
            ORDER BY tsc."AttackPower" DESC, tsc."Id";
            """;
        var memRows = await _dbContext.QueryAsync<ConfirmedMemberRow>(memSql, new { ids });
        var memsByTeam = memRows.GroupBy(r => r.TeamSlotId).ToDictionary(g => g.Key, g =>
            g.Select(x => new OpenTeamMemberDto { Job = x.Job, AttackPower = x.AttackPower, MapleBlessingLevel = x.MapleBlessingLevel }).ToList());

        foreach (var t in teams)
        {
            t.Requirements = byTeam.GetValueOrDefault(t.TeamSlotId, []);
            t.ConfirmedMembers = memsByTeam.GetValueOrDefault(t.TeamSlotId, []);
        }

        return teams;
    }

    public async Task<IEnumerable<LedTeamDto>> GetLedTeamsAsync(ulong leaderDiscordId)
    {
        // LeaderDiscordId=本人 的隊；LEFT JOIN 成員一次算三種狀態計數（避免 N+1）。period-less §8 Phase 4a：時間窗取代 period。
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
            WHERE ts."LeaderDiscordId" = @leaderDiscordId
              AND ( (ts."Kind" = 'Scheduled' AND ts."SlotDateTime" > now() - interval '1 day')
                 OR (ts."Kind" = 'Instant'   AND ts."ExpiresAt"   > now()) )
            GROUP BY ts."Id", ts."BossId", b."Name", ts."SlotDateTime", b."RequireMembers", ts."Description"
            ORDER BY ts."SlotDateTime";
            """;
        return await _dbContext.QueryAsync<LedTeamDto>(sql, new { leaderDiscordId = (long)leaderDiscordId });
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

    public async Task<IEnumerable<RosterMemberDto>> GetRosterAsync(int teamSlotId, ulong excludeDiscordId)
    {
        // 轉讓對象只能是 Confirmed 成員（已入隊才承接得了隊長）；排除自己（隊長本身也是 Confirmed）。
        const string sql = """
            SELECT tsc."Id" AS "MemberId", tsc."CharacterName" AS "CharacterName", p."DiscordName" AS "DiscordName"
            FROM "TeamSlotCharacter" tsc
            JOIN "Player" p ON p."DiscordId" = tsc."DiscordId"
            WHERE tsc."TeamSlotId" = @teamSlotId
              AND tsc."Status" = 'Confirmed'
              AND tsc."DiscordId" <> @excludeDiscordId
            ORDER BY tsc."Id";
            """;
        return await _dbContext.QueryAsync<RosterMemberDto>(sql, new { teamSlotId, excludeDiscordId = (long)excludeDiscordId });
    }

    public async Task<IEnumerable<TeamMemberDto>> GetConfirmedMembersAsync(int teamSlotId)
    {
        // 隊長排最前；不回 DiscordName（§9.12：隊友只看角色/職業，不露 Discord 身分）。
        const string sql = """
            SELECT p."DiscordName" AS "DiscordName", tsc."CharacterName" AS "CharacterName", tsc."Job" AS "Job",
                   tsc."AttackPower" AS "AttackPower", COALESCE(ch."MapleBlessingLevel", 0) AS "MapleBlessingLevel",
                   (tsc."DiscordId" = ts."LeaderDiscordId") AS "IsLeader"
            FROM "TeamSlotCharacter" tsc
            JOIN "TeamSlot" ts ON ts."Id" = tsc."TeamSlotId"
            JOIN "Player" p    ON p."DiscordId" = tsc."DiscordId"
            LEFT JOIN "Character" ch ON ch."Id" = tsc."CharacterId"
            WHERE tsc."TeamSlotId" = @teamSlotId AND tsc."Status" = 'Confirmed'
            ORDER BY (tsc."DiscordId" = ts."LeaderDiscordId") DESC, tsc."Id";
            """;
        return await _dbContext.QueryAsync<TeamMemberDto>(sql, new { teamSlotId });
    }

    public async Task<IEnumerable<OpenTeamRequirementDto>> GetRequirementsAsync(int teamSlotId)
    {
        const string sql = """
            SELECT r."Id" AS "RequirementId", r."Count" AS "Count", r."MinClearCount" AS "MinClearCount",
                   j."Job" AS "Job", j."MinAttackPower" AS "MinAttackPower"
            FROM "TeamSlotRequirement" r
            LEFT JOIN "TeamSlotRequirementJob" j ON j."RequirementId" = r."Id"
            WHERE r."TeamSlotId" = @teamSlotId
            ORDER BY r."Id";
            """;
        var rows = (await _dbContext.QueryAsync<ReqRow>(sql, new { teamSlotId })).ToList();
        return rows.GroupBy(r => r.RequirementId).Select(rg =>
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
        }).ToList();
    }

    public async Task<IEnumerable<string>> GetConfirmedJobsAsync(int teamSlotId)
    {
        const string sql = """
            SELECT tsc."Job"
            FROM "TeamSlotCharacter" tsc
            WHERE tsc."TeamSlotId" = @teamSlotId AND tsc."Status" = 'Confirmed';
            """;
        return await _dbContext.QueryAsync<string>(sql, new { teamSlotId });
    }

    private class ConfirmedMemberRow
    {
        public int TeamSlotId { get; set; }
        public string Job { get; set; } = "";
        public int AttackPower { get; set; }
        public int MapleBlessingLevel { get; set; }
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
