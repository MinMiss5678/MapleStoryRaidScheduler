using Domain.Repositories;
using Infrastructure.Entities;
using Infrastructure.Dapper;
using Infrastructure.Services;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class SessionRepository : ISessionRepository
{
    private readonly DbContext _dbContext;

    public SessionRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> CreateAsync(string sessionId, ulong discordId)
    {
        // 不存 Discord token（登入後沒用到、且明文憑證是安全負擔）——只存身分 + 我的 session 有效期
        return await _dbContext.Repository<SessionDbModel>().InsertAsync(new SessionDbModel()
        {
            SessionId = sessionId,
            DiscordId = (long)discordId,
            SessionExpiry = DateTimeOffset.UtcNow.Add(SessionPolicy.Lifetime)
        });
    }

    public async Task<int> ExtendAsync(string sessionId, DateTimeOffset sessionExpiry)
    {
        // sliding：延展有效期（只在過門檻時被呼叫，見 SessionService.TrySlideAsync）
        var sql = new UpdateBuilder<SessionDbModel>();
        sql.Set(x => x.SessionExpiry, sessionExpiry)
            .Where(x => x.SessionId == sessionId);

        return await _dbContext.ExecuteAsync(sql);
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
