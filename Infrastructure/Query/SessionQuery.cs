using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Query;

public class SessionQuery : ISessionQuery
{
    private readonly IUnitOfWork _unitOfWork;

    public SessionQuery(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
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

        return await _unitOfWork.QuerySingleOrDefaultAsync<Session>(sql);
    }
}