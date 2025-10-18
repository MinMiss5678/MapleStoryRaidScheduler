using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.BackgroundJobs;

public class DailyNotificationService : BackgroundService
{
    private readonly IDiscordService _discordService;
    private readonly ITeamSlotQuery _teamSlotQuery;

    public DailyNotificationService(IDiscordService discordService, ITeamSlotQuery teamSlotQuery)
    {
        _discordService = discordService;
        _teamSlotQuery = teamSlotQuery;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.Now;
            var nextRun = now.Date.AddHours(9);
        
            if (now > nextRun)
                nextRun = nextRun.AddDays(1);
            var delay = nextRun - now;

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            await SendDailyNotification();
        }
    }

    private async Task SendDailyNotification()
    {
        var slotDateTime = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var results = await _teamSlotQuery.GetBySlotDateTimeAsync(slotDateTime);
        
        var taipeiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
        // 將同一隊伍的玩家聚合到一條訊息
        var grouped = results
            .GroupBy(x => x.TeamSlotId)
            .Select(g => new TeamSlotDiscord()
            {
                SlotDateTime = TimeZoneInfo.ConvertTime(g.First().SlotDateTime, taipeiTimeZone),
                BossName = g.First().BossName,
                DiscordIds = g.Select(x => x.DiscordId).ToList()
            });
        
        if (!grouped.Any()) return;
        
        var messageBuilder = new System.Text.StringBuilder();
        messageBuilder.AppendLine($"📢 **今日 {slotDateTime:yyyy-MM-dd} 隊伍通知**");
        messageBuilder.AppendLine("————————————————————");
        
        foreach (var team in grouped)
        {
            var mentionsWithNames = new List<string>();
        
            foreach (var discordId in team.DiscordIds)
            {
                mentionsWithNames.Add($"<@{discordId}>");
            }
        
            var mentionsText = string.Join(" ", mentionsWithNames);
            messageBuilder.AppendLine($"{mentionsText}");
            messageBuilder.AppendLine($"Boss: **{team.BossName}**");
            messageBuilder.AppendLine($"時間: {team.SlotDateTime:HH:mm}");
            messageBuilder.AppendLine("————————————————————");
        }
        
        await _discordService.SendMessageAsync(messageBuilder.ToString());
    }
}