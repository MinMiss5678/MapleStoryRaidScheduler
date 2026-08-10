using Application.Interface;
using Application.Options;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly IDiscordOAuthClient _discordClient;
    private readonly ISessionService _sessionService;
    private readonly IDiscordRoleMappingRepository _roleMappingRepository;
    private readonly IJwtService _jwtService;
    private readonly IPlayerRepository _playerRepository;

    public AuthService(IDiscordOAuthClient discordClient, ISessionService sessionService, IDiscordRoleMappingRepository roleMappingRepository, IJwtService jwtService, IPlayerRepository playerRepository)
    {
        _discordClient = discordClient;
        _sessionService = sessionService;
        _roleMappingRepository = roleMappingRepository;
        _jwtService = jwtService;
        _playerRepository = playerRepository;
    }

    public async Task<DiscordUser> ExchangeCodeAsync(string code)
    {
        // OAuth 換到的 token 只在此處用一次（抓使用者身分）；不再存進 session（登入後沒用到、明文憑證是負擔）
        var tokenResponse = await _discordClient.ExchangeCodeAsync(code);
        var userDto = await _discordClient.GetUserAsync(tokenResponse.AccessToken);
        return new DiscordUser()
        {
            Id = userDto.Id,
            Name = userDto.Username,
            GlobalName = userDto.GlobalName,
        };
    }

    public async Task<string> CreateSessionAsync(ulong discordId)
        => await _sessionService.CreateAsync(discordId);

    public string CreateJwt(DiscordUser discordUser, string role)
        => _jwtService.CreateToken(discordUser, role);

    public async Task<bool> DeleteSessionAsync(string sessionId, string discordId)
        => await _sessionService.DeleteAsync(sessionId, discordId);

    public async Task<string?> RefreshToken(ulong discordId)
    {
        var member = await _discordClient.GetGuildMemberAsync(discordId);
        var roleIds = member.Roles
            .Select(r =>
            {
                if (ulong.TryParse(r, out var id)) return (ulong?)id;
                return null;
            })
            .Where(id => id.HasValue)
            .Select(id => id!.Value);

        var role = await _roleMappingRepository.ResolveRoleAsync(roleIds);

        if (role != null)
        {
            var player = await _playerRepository.GetAsync(discordId);
            return CreateJwt(new DiscordUser()
            {
                Id = discordId,
                Name = player?.DiscordName ?? string.Empty,
            }, role);
        }

        return null;
    }
}
