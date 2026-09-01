using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;

namespace Infrastructure.Repositories;

public class PlayerRepository : IPlayerRepository
{
    private readonly DbContext _dbContext;

    public PlayerRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ExistAsync(ulong discordId)
    {
        return await _dbContext.Repository<PlayerDbModel>().ExistAsync((long)discordId);
    }

    public async Task<int> CreateAsync(Player player)
    {
        // upsert：既有玩家更新 DiscordName（讓公會暱稱在重登時刷新）；Role 不在此覆蓋
        // ——登入流程另行決定角色、既有角色由 DB 沿用。同時消除舊 check-then-insert 的 TOCTOU（並發首登撞 23505）。
        const string sql = """
            INSERT INTO "Player" ("DiscordId", "DiscordName", "Role")
            VALUES (@DiscordId, @DiscordName, @Role)
            ON CONFLICT ("DiscordId") DO UPDATE SET "DiscordName" = EXCLUDED."DiscordName"
            """;
        return await _dbContext.ExecuteAsync(sql,
            new { DiscordId = (long)player.DiscordId, player.DiscordName, player.Role });
    }

    public async Task<Player?> GetAsync(ulong discordId)
    {
        var player = await _dbContext.Repository<PlayerDbModel>().GetByIdAsync((long)discordId);
        if (player == null) return null;

        return new Player()
        {
            DiscordId = (ulong)player.DiscordId,
            DiscordName = player.DiscordName,
            Role = player.Role
        };
    }

    public async Task<int> UpdateRoleAsync(ulong discordId, string role)
    {
        const string sql = "UPDATE \"Player\" SET \"Role\"=@Role WHERE \"DiscordId\"=@DiscordId";
        return await _dbContext.ExecuteAsync(sql, new { Role = role, DiscordId = (long)discordId });
    }

    public async Task BumpLastAffirmedAsync(ulong discordId)
    {
        // 節流：LastAffirmedAt 為 NULL 或已舊於 1 天才真寫 → 同玩家同日多次動作只寫一次（避免寫放大）。
        // 只碰 LastAffirmedAt 一欄（不動 DiscordName/Role），與 UpdateRoleAsync 同 targeted 風格。
        const string sql = """
            UPDATE "Player" SET "LastAffirmedAt" = now()
            WHERE "DiscordId" = @DiscordId
              AND ("LastAffirmedAt" IS NULL OR "LastAffirmedAt" < now() - interval '1 day')
            """;
        await _dbContext.ExecuteAsync(sql, new { DiscordId = (long)discordId });
    }

    public async Task<IReadOnlyCollection<ulong>> GetFreshnessNudgeTargetsAsync(int nudgeAfterDays)
    {
        const string sql = """
            SELECT p."DiscordId"
            FROM "Player" p
            WHERE p."LastAffirmedAt" IS NOT NULL
              AND p."LastAffirmedAt" < now() - make_interval(days => @nudgeAfterDays)
              AND (p."FreshnessNudgedAt" IS NULL OR p."FreshnessNudgedAt" <= p."LastAffirmedAt")
              AND EXISTS (SELECT 1 FROM "Character" c WHERE c."DiscordId" = p."DiscordId" AND c."IsSeekingRaid")
            """;
        var ids = await _dbContext.QueryAsync<long>(sql, new { nudgeAfterDays });
        return ids.Select(x => (ulong)x).ToList();
    }

    public async Task MarkFreshnessNudgedAsync(ulong discordId)
    {
        const string sql = """UPDATE "Player" SET "FreshnessNudgedAt" = now() WHERE "DiscordId" = @DiscordId""";
        await _dbContext.ExecuteAsync(sql, new { DiscordId = (long)discordId });
    }
}
