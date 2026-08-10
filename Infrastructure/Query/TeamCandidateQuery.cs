using Application.Queries;
using Domain.Entities;
using Infrastructure.Dapper;

namespace Infrastructure.Query;

public class TeamCandidateQuery : ITeamCandidateQuery
{
    private readonly DbContext _dbContext;

    public TeamCandidateQuery(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<CandidatePoolItem>> GetPoolAsync(int periodId, int bossId)
    {
        // boss-agnostic：撈該週期報名池所有角色 × 其玩家的時段；bossId 只用來算「本王總通關」（跨該玩家角色加總）。
        // 同角色若報名多王 → CharacterRegister 多列 → char×avail 會重複，用 DISTINCT 收斂。
        const string sql = """
            WITH clear_total AS (
                SELECT ch."DiscordId", SUM(cbc."ClearCount") AS total
                FROM "CharacterBossClear" cbc
                JOIN "Character" ch ON ch."Id" = cbc."CharacterId"
                WHERE cbc."BossId" = @bossId
                GROUP BY ch."DiscordId"
            )
            SELECT DISTINCT
                c."Id"                       AS "CharacterId",
                c."Name"                     AS "CharacterName",
                p."DiscordName"              AS "DiscordName",
                c."Job"                      AS "Job",
                c."AttackPower"              AS "AttackPower",
                c."MapleBlessingLevel"       AS "MapleBlessingLevel",
                COALESCE(ct.total, 0)::int   AS "BossClearCount",
                a."Weekday"                  AS "Weekday",
                a."StartTime"                AS "StartTime",
                a."EndTime"                  AS "EndTime"
            FROM "PlayerRegister" pr
            JOIN "CharacterRegister" cr ON cr."PlayerRegisterId" = pr."Id"
            JOIN "Character" c          ON c."Id" = cr."CharacterId"
            JOIN "Player" p             ON p."DiscordId" = c."DiscordId"
            JOIN "PlayerAvailability" a ON a."PlayerRegisterId" = pr."Id"
            LEFT JOIN clear_total ct    ON ct."DiscordId" = c."DiscordId"
            WHERE pr."PeriodId" = @periodId;
            """;

        var rows = await _dbContext.QueryAsync<PoolRow>(sql, new { periodId, bossId });

        // 同角色多時段 → 多列，group 回一筆 CandidatePoolItem（帶其時段清單）
        return rows
            .GroupBy(r => r.CharacterId)
            .Select(g =>
            {
                var first = g.First();
                return new CandidatePoolItem
                {
                    CharacterId = first.CharacterId,
                    CharacterName = first.CharacterName,
                    DiscordName = first.DiscordName,
                    Job = first.Job,
                    AttackPower = first.AttackPower,
                    MapleBlessingLevel = first.MapleBlessingLevel,
                    BossClearCount = first.BossClearCount,
                    Availabilities = g.Select(r => new PlayerAvailability
                    {
                        Weekday = r.Weekday,
                        StartTime = r.StartTime,
                        EndTime = r.EndTime
                    }).ToList()
                };
            })
            .ToList();
    }

    private class PoolRow
    {
        public string CharacterId { get; set; } = "";
        public string CharacterName { get; set; } = "";
        public string? DiscordName { get; set; }
        public string Job { get; set; } = "";
        public int AttackPower { get; set; }
        public int MapleBlessingLevel { get; set; }
        public int BossClearCount { get; set; }
        public int Weekday { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
    }
}
