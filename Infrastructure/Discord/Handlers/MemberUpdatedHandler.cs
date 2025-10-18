using Application.Interface;
using Application.Options;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Options;

namespace Presentation.Infrastructure.Discord.Handlers;

public class MemberUpdatedHandler : IEventHandler<GuildMemberUpdatedEventArgs>
{
    private readonly ISessionService _sessionService;
    private readonly DiscordOptions _discordOptions;

    public MemberUpdatedHandler(ISessionService sessionService, IOptions<DiscordOptions> discordOptions)
    {
        _sessionService = sessionService;
        _discordOptions = discordOptions.Value;
    }
    
    public async Task HandleEventAsync(DiscordClient sender, GuildMemberUpdatedEventArgs eventArgs)
    {
        bool wasAdmin = eventArgs.RolesBefore.Any(r => r.Id == Convert.ToUInt64(_discordOptions.AdminRoleId));
        bool isAdmin = eventArgs.RolesAfter.Any(r => r.Id == Convert.ToUInt64(_discordOptions.AdminRoleId));
    
        if (wasAdmin && !isAdmin)
        {
            await _sessionService.DeleteByDiscordAsync(eventArgs.Member.Id);
        }
    }
}