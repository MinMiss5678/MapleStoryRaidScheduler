using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.BackgroundJobs;

public class DiscordBotService : BackgroundService
{
    private readonly DiscordClient _discordClient;
    
    public DiscordBotService(DiscordClient discordClient)
    {
        _discordClient = discordClient;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _discordClient.ConnectAsync(new DiscordActivity("with fire", DiscordActivityType.Playing), DiscordUserStatus.Online);
        
        // 保持運作直到 Host 停止
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}