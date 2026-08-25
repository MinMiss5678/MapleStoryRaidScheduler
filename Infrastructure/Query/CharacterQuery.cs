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
            x.AttackPower,
            x.Level,
            x.IsSeekingRaid
        })
            .From<CharacterDbModel>()
            .Where<CharacterDbModel>(x => x.DiscordId == (long)discordId);

        return await _dbContext.QueryAsync<Character>(sql);
    }

    public async Task<Character?> GetByIdAsync(string id)
    {
        var sql = new QueryBuilder();
        sql.Select<CharacterDbModel>(x => new { x.Id, x.DiscordId, x.Name, x.Job, x.AttackPower, x.Level })
            .From<CharacterDbModel>()
            .Where<CharacterDbModel>(x => x.Id == id);
        return (await _dbContext.QueryAsync<Character>(sql)).FirstOrDefault();
    }

    public async Task<IEnumerable<CharacterDto>> GetWithDiscordNameAsync(ulong discordId, int? bossId = null)
    {
        // period-less（Phase 4d）：報名/週期退場 → 不再聚合「當期 Rounds / 已報名週期」。
        // 純角色 + 玩家顯示名；Rounds/RegisteredPeriodIds 留 DTO 預設（0 / 空），bossId 參數保留但不再過濾。
        const string sql = """
            SELECT a."Id", a."DiscordId", a."Name", a."Job", a."AttackPower", a."Level", b."DiscordName"
            FROM "Character" a
            LEFT JOIN "Player" b ON a."DiscordId" = b."DiscordId"
            WHERE a."DiscordId" = @discordId
            ORDER BY a."Id";
            """;

        return await _dbContext.QueryAsync<CharacterDto>(sql, new { discordId = (long)discordId });
    }
}
