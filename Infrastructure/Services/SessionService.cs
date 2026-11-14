using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Services;

public class SessionService : ISessionService
{
    private readonly ISessionRepository _sessionRepository;
    private readonly ISessionQuery _sessionQuery;
    private readonly ISessionCache _sessionCache;

    public SessionService(ISessionRepository sessionRepository, ISessionQuery sessionQuery, ISessionCache sessionCache)
    {
        _sessionRepository = sessionRepository;
        _sessionQuery = sessionQuery;
        _sessionCache = sessionCache;
    }

    public async Task<string> CreateAsync(ulong discordId)
    {
        var sessionId = Guid.NewGuid().ToString("N");

        await _sessionRepository.CreateAsync(sessionId, discordId);

        return sessionId;
    }

    public async Task<Session?> GetAsync(string sessionId, string discordId)
    {
        // 快取存 Redis（跨 pod 共享）→ 撤銷一次刪除即在所有 pod 生效（見 ISessionCache）。
        // 快取以 discordId 為 key、只存一筆，但資料庫允許同一人多筆 session（每登入一次一筆）
        // → 快取命中但 sessionId 不符時，不能直接拒絕（會擋掉第二台裝置的合法 session，造成登入迴圈），要回資料庫確認。
        var cached = await _sessionCache.GetAsync(discordId);
        if (cached != null && cached.SessionId == sessionId)
        {
            if (!IsValid(cached))
                return null;
            // sliding：只有「過門檻才續」時才寫（DB + 快取）；否則純讀命中，不寫（不打架讀穿快取）。
            if (await TrySlideAsync(sessionId, cached))
                await CacheAsync(discordId, cached);
            return cached;
        }

        // 偽造的 sessionId 在資料庫查不到 → 仍然拒絕（守 c0e0311 的 session 冒用防護）。
        var session = await _sessionQuery.GetAsync(sessionId);
        if (session == null)
            return null;

        // 歸屬核對：session 必須屬於 cookie 的 discordId。否則別人可用自己的 sessionId 把 session 寫進他人的快取位置（快取汙染）。
        if (session.DiscordId.ToString() != discordId)
            return null;

        // session 有效性 = SessionExpiry（我的授權政策）。過期即失效——不再用 Discord token 續期。
        if (!IsValid(session))
            return null;

        await TrySlideAsync(sessionId, session); // 可能延展 session.SessionExpiry
        // 快取已存放同一人的另一個 session 時不覆寫：兩台裝置交替使用會讓快取來回翻、等於失效；第二台裝置改為每次查資料庫。
        if (cached == null)
            await CacheAsync(discordId, session); // 首次讀入快取（帶現有/延展後的有效期）
        return session;
    }

    private static bool IsValid(Session session) => DateTimeOffset.UtcNow < session.SessionExpiry;

    /// <summary>節流 sliding：剩餘 &lt; 門檻才把 SessionExpiry 延展成 now + Lifetime 並寫 DB。回傳是否延展。</summary>
    private async Task<bool> TrySlideAsync(string sessionId, Session session)
    {
        if (session.SessionExpiry - DateTimeOffset.UtcNow >= SessionPolicy.SlideThreshold)
            return false;

        session.SessionExpiry = DateTimeOffset.UtcNow.Add(SessionPolicy.Lifetime);
        await _sessionRepository.ExtendAsync(sessionId, session.SessionExpiry);
        return true;
    }

    // 快取 TTL = 短固定新鮮度，且不活過有效期（撤銷殘留上界 = 此短 TTL，非 session 有效期）。
    private async Task CacheAsync(string discordId, Session session)
    {
        var remaining = session.SessionExpiry - DateTimeOffset.UtcNow;
        var ttl = remaining < SessionPolicy.CacheFreshness ? remaining : SessionPolicy.CacheFreshness;
        await _sessionCache.SetAsync(discordId, session, ttl);
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
