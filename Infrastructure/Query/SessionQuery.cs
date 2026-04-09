using Application.Queries;
using Domain.Entities;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Query;

public class SessionQuery : ISessionQuery
{
    private readonly DbContext _dbContext;

    public SessionQuery(DbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    public async Task<Session?> GetAsync(string sessionId)
    {
        var sql = new QueryBuilder();
        sql.Select<SessionDbModel>(x => new
            {
                x.DiscordId,
                x.RefreshToken,
                x.Expiry
            })
            .From<SessionDbModel>()
            .Where<SessionDbModel>(x => x.SessionId == sessionId);

        return await _dbContext.QuerySingleOrDefaultAsync<Session>(sql);
    }
}