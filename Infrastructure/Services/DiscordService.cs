using Application.Interface;
using Application.Options;
using DSharpPlus;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class DiscordService : IDiscordService
{
    private readonly DiscordClient _discordClient;
    private readonly DiscordOptions _discordOptions;

    public DiscordService(DiscordClient discordClient, IOptions<DiscordOptions> discordOptions)
    {
        _discordClient = discordClient; 
        _discordOptions = discordOptions.Value;
    }
    
    public async Task SendMessageAsync(string message)
    {
        var channel = await _discordClient.GetChannelAsync(Convert.ToUInt64(_discordOptions.ChannelId));
        await channel.SendMessageAsync(message);
    }
}