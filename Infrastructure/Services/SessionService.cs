using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Services;

public class SessionService : ISessionService
{
    private readonly ISessionRepository _sessionRepository;
    private readonly ISessionQuery _sessionQuery;
    private readonly IDiscordOAuthClient _discordClient;
    private readonly ISessionCache _sessionCache;

    public SessionService(ISessionRepository sessionRepository, ISessionQuery sessionQuery, IDiscordOAuthClient discordClient, ISessionCache sessionCache)
    {
        _sessionRepository = sessionRepository;
        _sessionQuery = sessionQuery;
        _discordClient = discordClient;
        _sessionCache = sessionCache;
    }

    public async Task<string> CreateAsync(ulong discordId, DiscordToken discordToken)
    {
        var sessionId = Guid.NewGuid().ToString("N");

        await _sessionRepository.CreateAsync(sessionId, discordId, discordToken);

        return sessionId;
    }

    public async Task<Session?> GetAsync(string sessionId, string discordId)
    {
        // 快取存 Redis（跨 pod 共享）→ 撤銷一次刪除即在所有 pod 生效（見 ISessionCache）。
        var cached = await _sessionCache.GetAsync(discordId);
        if (cached != null)
            return cached;

        var session = await _sessionQuery.GetAsync(sessionId);
        if (session == null)
            return null;

        // 快取 TTL 對應 AccessToken 過期
        var ttl = session.Expiry - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero)
            ttl = TimeSpan.FromMinutes(1); // 避免負值

        // 已過期 → 用 RefreshToken 換新 token、更新 DB、回傳新 session
        if (DateTimeOffset.UtcNow >= session.Expiry)
        {
            var newToken = await _discordClient.RefreshTokenAsync(session.RefreshToken);
            if (newToken == null)
                return null;

            var newSession = new Session()
            {
                SessionId = session.SessionId,
                DiscordId = session.DiscordId,
                AccessToken = newToken.AccessToken,
                RefreshToken = newToken.RefreshToken,
                Expiry = DateTimeOffset.UtcNow.AddSeconds(newToken.ExpiresIn),
            };

            await _sessionRepository.UpdateAsync(newSession);
            await _sessionCache.SetAsync(discordId, newSession, TimeSpan.FromSeconds(newToken.ExpiresIn));

            return newSession;
        }

        await _sessionCache.SetAsync(discordId, session, ttl);
        return session;
    }

    public async Task<bool> DeleteAsync(string sessionId, string discordId)
    {
        // 先刪 DB（真實來源）再清共享快取——cache-aside 慣例，縮小「刪快取後被舊值回填」的窗口。
        var deleted = await _sessionRepository.DeleteAsync(sessionId);
        await _sessionCache.RemoveAsync(discordId);
        return deleted;
    }

    public async Task DeleteByDiscordAsync(ulong discordId)
    {
        await _sessionRepository.DeleteByDiscordAsync(discordId);
        await _sessionCache.RemoveAsync(discordId.ToString());
    }
}
