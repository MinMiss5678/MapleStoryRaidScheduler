using Domain.Entities;

namespace Application.Queries;

public interface ISessionQuery
{
    Task<Session?> GetAsync(string sessionId);
}