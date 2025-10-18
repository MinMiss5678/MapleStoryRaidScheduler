using Application.Interface;
using Domain.Repositories;
using Infrastructure.Entities;
using Domain.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class SessionRepository : ISessionRepository
{
    private readonly IUnitOfWork _unitOfWork;

    public SessionRepository(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<int> CreateAsync(string sessionId, ulong discordId, DiscordToken token)
    {
        return await _unitOfWork.Repository<SessionDbModel>().InsertAsync(new SessionDbModel()
        {
            SessionId = sessionId,
            DiscordId = (long)discordId,
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            Expiry = DateTime.UtcNow.AddSeconds(token.ExpiresIn)
        });
    }
    
    public async Task<int> UpdateAsync(Session session)
    {
        var sql = new UpdateBuilder<SessionDbModel>();
        sql.Set(x=>x.SessionId, session.SessionId)
            .Set(x=>x.DiscordId, (long)session.DiscordId)
            .Set(x=>x.AccessToken, session.AccessToken)
            .Set(x=>x.RefreshToken, session.RefreshToken)
            .Set(x=>x.Expiry, session.Expiry)
            .Where(x=>x.SessionId == session.SessionId);

        return await _unitOfWork.ExecuteAsync(sql); 
    }

    public async Task<bool> DeleteAsync(string id)
    {
        return await _unitOfWork.Repository<SessionDbModel>().DeleteAsync(id);
    }

    public async Task DeleteByDiscordAsync(ulong discordId)
    {
        var sql = new DeleteBuilder<SessionDbModel>();
        sql.Where(x=>x.DiscordId == (long)discordId);

        await _unitOfWork.ExecuteAsync(sql);
    }
}