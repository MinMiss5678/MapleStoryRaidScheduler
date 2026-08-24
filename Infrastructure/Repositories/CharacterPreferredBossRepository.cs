using Domain.Repositories;
using Infrastructure.Dapper;

namespace Infrastructure.Repositories;

public class CharacterPreferredBossRepository : ICharacterPreferredBossRepository
{
    private readonly DbContext _dbContext;

    public CharacterPreferredBossRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<int>> GetBossIdsByCharacterAsync(string characterId)
    {
        const string sql = """
            SELECT "BossId" FROM "CharacterPreferredBoss" WHERE "CharacterId" = @characterId;
            """;
        return await _dbContext.QueryAsync<int>(sql, new { characterId });
    }

    public async Task ReplaceAsync(string characterId, IEnumerable<int> bossIds)
    {
        // 整批取代：先刪後插，同 UoW 交易（DbContext 綁請求交易）→ 原子。
        await _dbContext.ExecuteAsync(
            """DELETE FROM "CharacterPreferredBoss" WHERE "CharacterId" = @characterId;""",
            new { characterId });

        var ids = bossIds.Distinct().ToList();
        if (ids.Count == 0) return;

        const string insert = """
            INSERT INTO "CharacterPreferredBoss" ("CharacterId", "BossId") VALUES (@characterId, @bossId);
            """;
        foreach (var bossId in ids)
            await _dbContext.ExecuteAsync(insert, new { characterId, bossId });
    }
}
