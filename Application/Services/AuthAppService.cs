using Application.DTOs;
using Application.Interface;
using Domain.Entities;
using Domain.Repositories;

namespace Application.Services;

public class AuthAppService : IAuthAppService
{
    // Discord 使用者名稱長度遠低於此（≤32）；取寬鬆上限只擋真正異常/惡意的超長值。
    private const int MaxDiscordNameLength = 100;

    private readonly IAuthService _authService;
    private readonly IDiscordOAuthClient _discordOAuthClient;
    private readonly IPlayerService _playerService;
    private readonly IDiscordRoleMappingRepository _roleMappingRepository;

    public AuthAppService(IAuthService authService, IDiscordOAuthClient discordOAuthClient,
        IPlayerService playerService, IDiscordRoleMappingRepository roleMappingRepository)
    {
        _authService = authService;
        _discordOAuthClient = discordOAuthClient;
        _playerService = playerService;
        _roleMappingRepository = roleMappingRepository;
    }

    public async Task<LoginResult> LoginAsync(string code)
    {
        var user = await _authService.ExchangeCodeAsync(code);

        var existingPlayer = await _playerService.GetAsync(user.Id);
        var member = await _discordOAuthClient.GetGuildMemberAsync(user.Id);

        // 顯示名優先序：公會暱稱(nick) → 帳號顯示名(global_name) → username。登入時決定、並透過 upsert
        // 更新 Player.DiscordName（既有成員重登即刷新）→ 系統各處顯示大家認得的公會暱稱。
        // 此共用 choke point 防禦性擋空/異常長（bot 路徑不經 WebApi DTO 驗證），沿用「無法安全登入→回失敗」。
        var displayName = new[] { member.Nick, user.GlobalName, user.Name }
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? user.Name;
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > MaxDiscordNameLength)
            return new LoginResult { IsSuccess = false };
        user.Name = displayName;   // 讓 JWT 與 Player.DiscordName 都用顯示名

        // 角色來源改為 DB 映射：
        // 1) 若玩家已存在，沿用 DB 中的 Player.Role
        // 2) 若是新玩家，依 Discord 身分組透過 DiscordRoleMapping 解析系統 Role
        string? role = existingPlayer?.Role;
        if (string.IsNullOrEmpty(role))
        {
            // 將 OAuth 回傳的身分組 ID 轉為 ulong 陣列供 DB 映射使用
            var roleIds = member.Roles
                .Select(r =>
                {
                    if (ulong.TryParse(r, out var id)) return (ulong?)id;
                    return null;
                })
                .Where(id => id.HasValue)
                .Select(id => id!.Value);

            role = await _roleMappingRepository.ResolveRoleAsync(roleIds);
        }

        if (string.IsNullOrEmpty(role))
        {
            // 無法從 DB 解析出系統角色，視為登入失敗
            return new LoginResult { IsSuccess = false };
        }

        await _playerService.CreateAsync(new Player()
        {
            DiscordId = user.Id,
            DiscordName = user.Name,
            Role = role
        });

        if (role == "admin")
        {
            var sessionId = await _authService.CreateSessionAsync(user.Id);
            return new LoginResult
            {
                IsSuccess = true,
                SessionId = sessionId,
                DiscordId = user.Id,
                Expiry = DateTimeOffset.UtcNow.AddDays(30),
                Role = role
            };
        }
        else
        {
            var jwt = _authService.CreateJwt(user, role);
            return new LoginResult
            {
                IsSuccess = true,
                JwtToken = jwt,
                DiscordId = user.Id,
                Expiry = DateTimeOffset.UtcNow.AddDays(30),
                Role = role
            };
        }
    }

    public async Task<bool> LogoutAsync(string sessionId, string discordId)
    {
        return await _authService.DeleteSessionAsync(sessionId, discordId);
    }
}
