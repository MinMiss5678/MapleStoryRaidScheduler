using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Services;

public class SessionService : ISessionService
{
    // 快取新鮮度：短固定 TTL（純加速 + 撤銷殘留上界），與 session 有效期無關。
    private static readonly TimeSpan CacheFreshness = TimeSpan.FromMinutes(15);

    private readonly ISessionRepository _sessionRepository;
    private readonly ISessionQuery _sessionQuery;
    private readonly ISessionCache _sessionCache;

    public SessionService(ISessionRepository sessionRepository, ISessionQuery sessionQuery, ISessionCache sessionCache)
    {
        _sessionRepository = sessionRepository;
        _sessionQuery = sessionQuery;
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
            return IsValid(cached) ? cached : null;   // 命中也檢查有效性（不靠 cache TTL 當有效期）

        var session = await _sessionQuery.GetAsync(sessionId);
        if (session == null)
            return null;

        // session 有效性 = SessionExpiry（我的授權政策）。過期即失效——
        // 不再用 Discord RefreshToken 續期（那把 auth 綁死第三方端點、且刷的是登入後沒用的 token）。
        if (!IsValid(session))
            return null;

        // 快取 TTL = 短固定新鮮度，且不活過有效期（撤銷殘留上界 = 此短 TTL，非 session 有效期、更非 Discord 7 天）。
        var remaining = session.SessionExpiry - DateTimeOffset.UtcNow;
        var ttl = remaining < CacheFreshness ? remaining : CacheFreshness;
        await _sessionCache.SetAsync(discordId, session, ttl);
        return session;
    }

    private static bool IsValid(Session session) => DateTimeOffset.UtcNow < session.SessionExpiry;

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
