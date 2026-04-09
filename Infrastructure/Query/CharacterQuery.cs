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

    public CharacterQuery(DbContext dbContext)
    {
        _dbContext = dbContext;
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
                x.AttackPower
            })
            .From<CharacterDbModel>()
            .Where<CharacterDbModel>(x => x.DiscordId == (long)discordId);

        return await _dbContext.QueryAsync<Character>(sql);
    }
    
    public async Task<IEnumerable<CharacterDto>> GetWithDiscordNameAsync(ulong discordId, int? bossId = null)
    {
        var bossFilter = bossId.HasValue ? "AND c.\"BossId\" = @bossId" : "";
        var sql = $"""
                           SELECT
                               a."Id",
                               a."DiscordId",
                               a."Name",
                               a."Job",
                               a."AttackPower",
                               b."DiscordName",
                               CAST(COALESCE(SUM(c."Rounds"), 0) AS INTEGER) AS "Rounds",
                               ARRAY_AGG(DISTINCT d."PeriodId") FILTER (WHERE d."PeriodId" IS NOT NULL) AS "RegisteredPeriodIds"
                           FROM "Character" a
                           LEFT JOIN "Player" b ON a."DiscordId" = b."DiscordId"
                           LEFT JOIN "CharacterRegister" c ON a."Id" = c."CharacterId" {bossFilter}
                           LEFT JOIN "PlayerRegister" d ON a."DiscordId" = d."DiscordId"
                           WHERE a."DiscordId" = @discordId
                           GROUP BY a."Id", a."DiscordId", a."Name", a."Job", a."AttackPower", b."DiscordName"
                           """;

        return await _dbContext.QueryAsync<CharacterDto>(sql, new { discordId = (long)discordId, bossId });
    }
}