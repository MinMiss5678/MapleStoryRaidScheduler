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
        // leader-led 模型：即時找隊不再公開別人（不外洩 Discord 身分）——只回「我自己」未過期的意圖，供本人檢視/取消。
        // 別人是由隊長開即時團時、透過候選（GetInstantPoolAsync）在 web 內被邀，不走公開看板+Discord 私敲。
        const string sql = """
            SELECT li."Id"                          AS "Id",
                   li."CharacterId"                 AS "CharacterId",
                   c."Name"                         AS "CharacterName",
                   c."Job"                          AS "Job",
                   c."AttackPower"                  AS "AttackPower",
                   li."BossId"                      AS "BossId",
                   b."Name"                         AS "BossName"
            FROM "LfgIntent" li
            JOIN "Character" c ON c."Id" = li."CharacterId"
            LEFT JOIN "Boss" b ON b."Id" = li."BossId"
            WHERE li."ExpiresAt" > now() AND li."DiscordId" = @me
            ORDER BY li."CreatedAt" DESC;
            """;
        return await _dbContext.QueryAsync<LfgBoardItemDto>(sql, new { me = (long)currentDiscordId });
    }
}
