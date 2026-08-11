using Application.DTOs;
using Application.Queries;
using Infrastructure.Dapper;

namespace Infrastructure.Query;

public class LfgQuery : ILfgQuery
{
    private readonly DbContext _dbContext;

    public LfgQuery(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<LfgBoardItemDto>> GetBoardAsync(ulong currentDiscordId)
    {
        // 未過期的找隊意圖（透明化：顯示玩家暱稱、角色、王）。IsMine 供前端標「我的」+ 取消。
        const string sql = """
            SELECT li."Id"                          AS "Id",
                   li."CharacterId"                 AS "CharacterId",
                   c."Name"                         AS "CharacterName",
                   p."DiscordName"                  AS "DiscordName",
                   c."Job"                          AS "Job",
                   c."AttackPower"                  AS "AttackPower",
                   li."BossId"                      AS "BossId",
                   b."Name"                         AS "BossName",
                   (li."DiscordId" = @me)           AS "IsMine"
            FROM "LfgIntent" li
            JOIN "Character" c ON c."Id" = li."CharacterId"
            JOIN "Player" p    ON p."DiscordId" = c."DiscordId"
            LEFT JOIN "Boss" b ON b."Id" = li."BossId"
            WHERE li."ExpiresAt" > now()
            ORDER BY li."CreatedAt" DESC;
            """;
        return await _dbContext.QueryAsync<LfgBoardItemDto>(sql, new { me = (long)currentDiscordId });
    }
}
