using Application.Interface;
using Application.Options;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Services;

public class SessionService : ISessionService
{
    private readonly ISessionRepository _sessionRepository;
    private readonly ISessionQuery _sessionQuery;
    private readonly IDiscordOAuthClient _discordClient;
    private readonly IMemoryCache _memoryCache;
    private readonly DiscordOptions _discordOptions;

    public SessionService(ISessionRepository sessionRepository, ISessionQuery sessionQuery, IDiscordOAuthClient discordClient, IMemoryCache memoryCache)
    {
        _sessionRepository = sessionRepository;
        _sessionQuery = sessionQuery;
        _discordClient = discordClient;
        _memoryCache = memoryCache;
    }

    public async Task<string> CreateAsync(ulong discordId, DiscordToken discordToken)
    {
        var sessionId = Guid.NewGuid().ToString("N");

        await _sessionRepository.CreateAsync(sessionId, discordId, discordToken);

        return sessionId;
    }

    public async Task<Session?> GetAsync(string sessionId, string discordId)
    {
        return await _memoryCache.GetOrCreateAsync($"sessionId{discordId}", async entry =>
        {
            var session = await _sessionQuery.GetAsync(sessionId);

            if (session == null)
                return null;

            // 設快取過期時間對應 AccessToken 過期
            var ttl = session.Expiry - DateTimeOffset.UtcNow;
            if (ttl <= TimeSpan.Zero)
                ttl = TimeSpan.FromMinutes(1); // 避免負值
            entry.AbsoluteExpirationRelativeToNow = ttl;

            // 自動刷新 token
            if (DateTimeOffset.UtcNow >= session.Expiry)
            {
                var newToken = await _discordClient.RefreshTokenAsync(session.RefreshToken);
                var newSession = new Session()
                {
                    DiscordId = session.DiscordId,
                    AccessToken = newToken.AccessToken,
                    RefreshToken = newToken.RefreshToken,
                    Expiry = DateTimeOffset.UtcNow.AddSeconds(newToken.ExpiresIn),
                };
                
                await _sessionRepository.UpdateAsync(newSession);

                // 更新快取 TTL
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(newToken.ExpiresIn);

                return newSession;
            }

            return session;
        });
    }

    public async Task<bool> DeleteAsync(string sessionId, string discordId)
    {
        _memoryCache.Remove( $"session{discordId}");
        return await _sessionRepository.DeleteAsync(sessionId);
    }
    
    public async Task DeleteByDiscordAsync(ulong discordId)
    {
        _memoryCache.Remove( $"session{discordId}");
        await _sessionRepository.DeleteByDiscordAsync(discordId);
    }
}