using Application.DTOs;
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

    public async Task<IEnumerable<CandidatePoolItem>> GetPoolAsync(int bossId)
    {
        // period-less（§8 Phase 2，B 案）：候選 = 參戰中(IsSeekingRaid)角色 × 其玩家的常設可用時段。
        // 不再吃 period/報名；bossId 只用來算「本王總通關」（跨該玩家角色加總）。同人多時段 → 多列，下面 group 收斂。
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
                c."DiscordId"                AS "DiscordId",
                p."DiscordName"              AS "DiscordName",
                c."Job"                      AS "Job",
                c."AttackPower"              AS "AttackPower",
                c."MapleBlessingLevel"       AS "MapleBlessingLevel",
                COALESCE(ct.total, 0)::int   AS "BossClearCount",
                (pbx."BossId" IS NOT NULL)   AS "PrefersThisBoss",
                EXISTS(SELECT 1 FROM "CharacterPreferredBoss" pb2 WHERE pb2."CharacterId" = c."Id") AS "HasAnyPreference",
                a."Weekday"                  AS "Weekday",
                a."StartTime"                AS "StartTime",
                a."EndTime"                  AS "EndTime"
            FROM "Character" c
            JOIN "Player" p                     ON p."DiscordId" = c."DiscordId"
            JOIN "PlayerAvailabilityStanding" a ON a."DiscordId" = c."DiscordId"
            LEFT JOIN clear_total ct            ON ct."DiscordId" = c."DiscordId"
            LEFT JOIN "CharacterPreferredBoss" pbx ON pbx."CharacterId" = c."Id" AND pbx."BossId" = @bossId
            WHERE c."IsSeekingRaid";
            """;

        var rows = await _dbContext.QueryAsync<PoolRow>(sql, new { bossId });

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
                    DiscordId = (ulong)first.DiscordId,
                    DiscordName = first.DiscordName,
                    Job = first.Job,
                    AttackPower = first.AttackPower,
                    MapleBlessingLevel = first.MapleBlessingLevel,
                    BossClearCount = first.BossClearCount,
                    PrefersThisBoss = first.PrefersThisBoss,
                    HasAnyPreference = first.HasAnyPreference,
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

    public async Task<IEnumerable<CandidatePoolItem>> GetInstantPoolAsync(int bossId)
    {
        // 即時團候選 = 未過期、找該王的意圖。無常設時段（他們是「現在」要打）。
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
                c."DiscordId"                AS "DiscordId",
                p."DiscordName"              AS "DiscordName",
                c."Job"                      AS "Job",
                c."AttackPower"              AS "AttackPower",
                c."MapleBlessingLevel"       AS "MapleBlessingLevel",
                COALESCE(ct.total, 0)::int   AS "BossClearCount",
                (pbx."BossId" IS NOT NULL)   AS "PrefersThisBoss",
                EXISTS(SELECT 1 FROM "CharacterPreferredBoss" pb2 WHERE pb2."CharacterId" = c."Id") AS "HasAnyPreference"
            FROM "LfgIntent" li
            JOIN "Character" c       ON c."Id" = li."CharacterId"
            JOIN "Player" p          ON p."DiscordId" = c."DiscordId"
            LEFT JOIN clear_total ct ON ct."DiscordId" = c."DiscordId"
            LEFT JOIN "CharacterPreferredBoss" pbx ON pbx."CharacterId" = c."Id" AND pbx."BossId" = @bossId
            WHERE li."ExpiresAt" > now() AND li."BossId" = @bossId;
            """;
        var rows = await _dbContext.QueryAsync<PoolRow>(sql, new { bossId });
        return rows
            .GroupBy(r => r.CharacterId)
            .Select(g =>
            {
                var first = g.First();
                return new CandidatePoolItem
                {
                    CharacterId = first.CharacterId,
                    CharacterName = first.CharacterName,
                    DiscordId = (ulong)first.DiscordId,
                    DiscordName = first.DiscordName,
                    Job = first.Job,
                    AttackPower = first.AttackPower,
                    MapleBlessingLevel = first.MapleBlessingLevel,
                    BossClearCount = first.BossClearCount,
                    PrefersThisBoss = first.PrefersThisBoss,
                    HasAnyPreference = first.HasAnyPreference,
                    Availabilities = [] // 即時：不看常設時段
                };
            })
            .ToList();
    }

    public async Task<IEnumerable<AvailabilityOverrideItem>> GetOverridesForDateAsync(DateOnly date)
    {
        const string sql = """
            SELECT "DiscordId", "StartTime", "EndTime", "IsAvailable"
            FROM "PlayerAvailabilityOverride"
            WHERE "Date" = @date;
            """;
        var rows = await _dbContext.QueryAsync<OverrideRow>(sql, new { date });
        return rows.Select(r => new AvailabilityOverrideItem
        {
            DiscordId = (ulong)r.DiscordId,
            StartTime = r.StartTime,
            EndTime = r.EndTime,
            IsAvailable = r.IsAvailable
        }).ToList();
    }

    public async Task<IReadOnlyCollection<ulong>> GetHighLeaveRateDiscordIdsAsync(
        IEnumerable<ulong> discordIds, DateTimeOffset windowStart, int minSample, int thresholdPercent)
    {
        var ids = discordIds.Select(x => (long)x).ToArray();
        if (ids.Length == 0) return [];

        // 窗內以 DiscordId 聚合：參加=Confirmed+Left、退團=Left；HAVING 濾出樣本足夠且率達門檻者。
        const string sql = """
            SELECT "DiscordId"
            FROM "TeamSlotCharacter"
            WHERE "DiscordId" = ANY(@ids)
              AND "Status" IN ('Confirmed', 'Left')
              AND "SlotDateTime" >= @windowStart
            GROUP BY "DiscordId"
            HAVING COUNT(*) >= @minSample
               AND COUNT(*) FILTER (WHERE "Status" = 'Left') * 100 >= @thresholdPercent * COUNT(*);
            """;
        var result = await _dbContext.QueryAsync<long>(sql, new { ids, windowStart, minSample, thresholdPercent });
        return result.Select(x => (ulong)x).ToHashSet();
    }

    private class OverrideRow
    {
        public long DiscordId { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public bool IsAvailable { get; set; }
    }

    private class PoolRow
    {
        public string CharacterId { get; set; } = "";
        public string CharacterName { get; set; } = "";
        public long DiscordId { get; set; }
        public string? DiscordName { get; set; }
        public string Job { get; set; } = "";
        public int AttackPower { get; set; }
        public int MapleBlessingLevel { get; set; }
        public int BossClearCount { get; set; }
        public bool PrefersThisBoss { get; set; }
        public bool HasAnyPreference { get; set; }
        public int Weekday { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
    }
}
