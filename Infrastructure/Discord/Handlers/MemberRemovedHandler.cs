using Application.Interface;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace Presentation.Infrastructure.Discord.Handlers;

public class MemberRemovedHandler : IEventHandler<GuildMemberRemovedEventArgs>
{
    private readonly ISessionService _sessionService;

    public MemberRemovedHandler(ISessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public async Task HandleEventAsync(DiscordClient sender, GuildMemberRemovedEventArgs eventArgs)
    {
        // 被踢出或自行離開伺服器，立即清除 session（對非 admin 無副作用）
        await _sessionService.DeleteByDiscordAsync(eventArgs.Member.Id);
    }
}
