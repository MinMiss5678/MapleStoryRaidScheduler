using Domain.Repositories;
using Infrastructure.Entities;
using Infrastructure.Dapper;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class SessionRepository : ISessionRepository
{
    // session 有效期 = 我的授權政策（絕對過期），與 Discord token TTL 無關
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    private readonly DbContext _dbContext;

    public SessionRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> CreateAsync(string sessionId, ulong discordId)
    {
        // 不再存 Discord token（登入後沒用到、且明文憑證是安全負擔）——只存身分 + 我的 session 有效期
        return await _dbContext.Repository<SessionDbModel>().InsertAsync(new SessionDbModel()
        {
            SessionId = sessionId,
            DiscordId = (long)discordId,
            SessionExpiry = DateTimeOffset.UtcNow.Add(SessionLifetime)
        });
    }

    public async Task<bool> DeleteAsync(string id)
    {
        return await _dbContext.Repository<SessionDbModel>().DeleteAsync(id);
    }

    public async Task DeleteByDiscordAsync(ulong discordId)
    {
        var sql = new DeleteBuilder<SessionDbModel>();
        sql.Where(x => x.DiscordId == (long)discordId);

        await _dbContext.ExecuteAsync(sql);
    }
}
