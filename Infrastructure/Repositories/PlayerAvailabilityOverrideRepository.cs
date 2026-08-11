using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;

namespace Infrastructure.Repositories;

public class PlayerAvailabilityOverrideRepository : IPlayerAvailabilityOverrideRepository
{
    private readonly DbContext _dbContext;

    public PlayerAvailabilityOverrideRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CreateAsync(PlayerAvailabilityOverride model)
    {
        const string sql = """
            INSERT INTO "PlayerAvailabilityOverride"("DiscordId","Date","StartTime","EndTime","IsAvailable")
            VALUES (@DiscordId, @Date, @StartTime, @EndTime, @IsAvailable);
            """;
        await _dbContext.ExecuteAsync(sql, new
        {
            DiscordId = (long)model.DiscordId,
            model.Date,
            model.StartTime,
            model.EndTime,
            model.IsAvailable
        });
    }

    public async Task<int> DeleteAsync(ulong discordId, int id)
    {
        const string sql = """DELETE FROM "PlayerAvailabilityOverride" WHERE "Id" = @id AND "DiscordId" = @discordId;""";
        return await _dbContext.ExecuteAsync(sql, new { id, discordId = (long)discordId });
    }

    public async Task<IEnumerable<PlayerAvailabilityOverride>> GetByDiscordIdAsync(ulong discordId)
    {
        // 不 SELECT DiscordId（呼叫者已知）→ 免 bigint→ulong 映射；Date/Time 由型別處理器轉。
        const string sql = """
            SELECT "Id", "Date", "StartTime", "EndTime", "IsAvailable"
            FROM "PlayerAvailabilityOverride"
            WHERE "DiscordId" = @discordId
            ORDER BY "Date", "StartTime";
            """;
        return await _dbContext.QueryAsync<PlayerAvailabilityOverride>(sql, new { discordId = (long)discordId });
    }
}
