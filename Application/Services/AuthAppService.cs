using Application.DTOs;
using Application.Interface;
using Domain.Entities;
using Domain.Repositories;

namespace Application.Services;

public class AuthAppService
{
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
        var (user, token) = await _authService.ExchangeCodeAsync(code);
        var existingPlayer = await _playerService.GetAsync(user.Id);
        var roles = await _discordOAuthClient.GetUserRolesAsync(user.Id);

        // 角色來源改為 DB 映射：
        // 1) 若玩家已存在，沿用 DB 中的 Player.Role
        // 2) 若是新玩家，依 Discord 身分組透過 DiscordRoleMapping 解析系統 Role
        string? role = existingPlayer?.Role;
        if (string.IsNullOrEmpty(role))
        {
            // 將 OAuth 回傳的身分組 ID 轉為 ulong 陣列供 DB 映射使用
            var roleIds = roles
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
            var sessionId = await _authService.CreateSessionAsync(user.Id, token);
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
            var jwt = _authService.CreateJwt(user);
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