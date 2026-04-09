using Application.Interface;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Domain.Repositories;

namespace Presentation.Infrastructure.Discord.Handlers;

public class MemberUpdatedHandler : IEventHandler<GuildMemberUpdatedEventArgs>
{
    private readonly ISessionService _sessionService;
    private readonly IDiscordRoleMappingRepository _roleMappingRepository;
    private readonly IPlayerService _playerService;

    public MemberUpdatedHandler(
        ISessionService sessionService,
        IDiscordRoleMappingRepository roleMappingRepository,
        IPlayerService playerService)
    {
        _sessionService = sessionService;
        _roleMappingRepository = roleMappingRepository;
        _playerService = playerService;
    }
    
    public async Task HandleEventAsync(DiscordClient sender, GuildMemberUpdatedEventArgs eventArgs)
    {
        var beforeIds = eventArgs.RolesBefore.Select(r => r.Id);
        var afterIds = eventArgs.RolesAfter.Select(r => r.Id);

        var beforeRole = await _roleMappingRepository.ResolveRoleAsync(beforeIds);
        var afterRole = await _roleMappingRepository.ResolveRoleAsync(afterIds);

        // Admin → 非 Admin，移除所有 Session
        if (string.Equals(beforeRole, "Admin", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(afterRole, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            await _sessionService.DeleteByDiscordAsync(eventArgs.Member.Id);
        }

        // 若角色有變化且有映射到系統角色，更新 Player.Role
        if (!string.Equals(beforeRole, afterRole, StringComparison.OrdinalIgnoreCase) && afterRole != null)
        {
            await _playerService.UpdateRoleAsync(eventArgs.Member.Id, afterRole);
        }
    }
}