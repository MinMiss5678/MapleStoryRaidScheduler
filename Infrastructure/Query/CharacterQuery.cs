using Application.DTOs;
using Application.Queries;
using Domain.Entities;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Query;

public class CharacterQuery : ICharacterQuery
{
    private readonly DbContext _dbContext;
    private readonly IPeriodQuery _periodQuery;

    public CharacterQuery(DbContext dbContext, IPeriodQuery periodQuery)
    {
        _dbContext = dbContext;
        _periodQuery = periodQuery;
    }

    public async Task<IEnumerable<Character>> GetByDiscordIdAsync(ulong discordId)
    {
        var sql = new QueryBuilder();
        sql.Select<CharacterDbModel>(x => new
        {
            x.Id,
            x.DiscordId,
            x.Name,
            x.Job,
            x.AttackPower,
            x.IsSeekingRaid
        })
            .From<CharacterDbModel>()
            .Where<CharacterDbModel>(x => x.DiscordId == (long)discordId);

        return await _dbContext.QueryAsync<Character>(sql);
    }

    public async Task<Character?> GetByIdAsync(string id)
    {
        var sql = new QueryBuilder();
        sql.Select<CharacterDbModel>(x => new { x.Id, x.DiscordId, x.Name, x.Job, x.AttackPower })
            .From<CharacterDbModel>()
            .Where<CharacterDbModel>(x => x.Id == id);
        return (await _dbContext.QueryAsync<Character>(sql)).FirstOrDefault();
    }

    public async Task<IEnumerable<CharacterDto>> GetWithDiscordNameAsync(ulong discordId, int? bossId = null)
    {
        // CTE 預先聚合當期 Rounds，避免 correlated subquery N 次執行
        var periodId = await _periodQuery.GetActivePeriodIdAsync();
        var bossFilter = bossId.HasValue ? "AND cr.\"BossId\" = @bossId" : "";
        var sql = $"""
                           WITH current_rounds AS (
                               SELECT cr."CharacterId", SUM(cr."Rounds") AS total
                               FROM "PlayerRegister" pr
                               JOIN "CharacterRegister" cr ON cr."PlayerRegisterId" = pr."Id"
                               WHERE pr."DiscordId" = @discordId
                                 AND pr."PeriodId" = @periodId
                                 {bossFilter}
                               GROUP BY cr."CharacterId"
                           )
                           SELECT
                               a."Id",
                               a."DiscordId",
                               a."Name",
                               a."Job",
                               a."AttackPower",
                               b."DiscordName",
                               CAST(COALESCE(r.total, 0) AS INTEGER) AS "Rounds",
                               ARRAY_AGG(DISTINCT d."PeriodId") FILTER (WHERE d."PeriodId" IS NOT NULL) AS "RegisteredPeriodIds"
                           FROM "Character" a
                           LEFT JOIN "Player" b ON a."DiscordId" = b."DiscordId"
                           LEFT JOIN current_rounds r ON r."CharacterId" = a."Id"
                           LEFT JOIN "PlayerRegister" d ON a."DiscordId" = d."DiscordId"
                           WHERE a."DiscordId" = @discordId
                           GROUP BY a."Id", a."DiscordId", a."Name", a."Job", a."AttackPower", b."DiscordName", r.total
                           """;

        return await _dbContext.QueryAsync<CharacterDto>(sql, new { discordId = (long)discordId, bossId, periodId });
    }
}
