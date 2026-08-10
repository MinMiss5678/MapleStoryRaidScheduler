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
}
