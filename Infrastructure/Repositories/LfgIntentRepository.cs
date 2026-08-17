using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;

namespace Infrastructure.Repositories;

public class LfgIntentRepository : ILfgIntentRepository
{
    private readonly DbContext _dbContext;

    public LfgIntentRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CreateAsync(LfgIntent intent)
    {
        // 同角色同王（含任意王=NULL）已有意圖 → 只刷新 TTL，不新增列（uq_lfgintent_char_boss，NULLS NOT DISTINCT）。
        const string sql = """
            INSERT INTO "LfgIntent"("DiscordId","CharacterId","BossId","ExpiresAt")
            VALUES (@DiscordId, @CharacterId, @BossId, @ExpiresAt)
            ON CONFLICT ("CharacterId","BossId") DO UPDATE
                SET "ExpiresAt" = EXCLUDED."ExpiresAt",
                    "CreatedAt" = now(),
                    "DiscordId" = EXCLUDED."DiscordId";
            """;
        await _dbContext.ExecuteAsync(sql, new
        {
            DiscordId = (long)intent.DiscordId,
            intent.CharacterId,
            intent.BossId,
            ExpiresAt = intent.ExpiresAt.ToUniversalTime()
        });
    }

    public async Task<int> DeleteAsync(ulong discordId, int id)
    {
        const string sql = """DELETE FROM "LfgIntent" WHERE "Id" = @id AND "DiscordId" = @discordId;""";
        return await _dbContext.ExecuteAsync(sql, new { id, discordId = (long)discordId });
    }

    public async Task DeleteByDiscordIdAsync(ulong discordId)
    {
        const string sql = """DELETE FROM "LfgIntent" WHERE "DiscordId" = @discordId;""";
        await _dbContext.ExecuteAsync(sql, new { discordId = (long)discordId });
    }

}
