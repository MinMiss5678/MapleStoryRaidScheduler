using Application.Events;
using Application.Interface;
using Application.Options;
using Application.Queries;
using Domain.Repositories;
using DSharpPlus;
using DSharpPlus.Extensions;
using Infrastructure.BackgroundJobs;
using Infrastructure.Dapper;
using Infrastructure.Query;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Presentation.Infrastructure.Discord.Handlers;

namespace Presentation;

public class Program
{
    static async Task Main()
    {
       var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            })
            .ConfigureServices((context, services) =>
            {
                var config = context.Configuration;
                var tokenFile = config["Discord:BotTokenFile"];

                if (!string.IsNullOrEmpty(tokenFile) && File.Exists(tokenFile))
                {
                    var token = File.ReadAllText(tokenFile).Trim();
              
                    var intents = DiscordIntents.AllUnprivileged |
                                  DiscordIntents.MessageContents |
                                  DiscordIntents.Guilds |
                                  DiscordIntents.GuildMembers;
                    services.AddDiscordClient(token, intents);
                }

                // 資料庫與 Repository 註冊
                var defaultConnectionFile = config["ConnectionStrings:DefaultConnectionFile"];
                if (!string.IsNullOrEmpty(defaultConnectionFile) && File.Exists(defaultConnectionFile))
                {
                    var defaultConnection = File.ReadAllText(defaultConnectionFile).Trim();
                    services.AddSingleton<NpgsqlConnection>(_ =>
                        new NpgsqlConnection(defaultConnection));
                }
                
                services.AddSingleton<IUnitOfWork, UnitOfWork>();
                services.AddSingleton<IDiscordService, DiscordService>();
                services.AddSingleton<ITeamSlotQuery, TeamSlotQuery>();
                services.AddSingleton<ISessionService, SessionService>();
                services.AddSingleton<ISessionRepository, SessionRepository>();
                services.AddSingleton<ISessionQuery, SessionQuery>();
                services.AddSingleton<IDiscordOAuthClient, DiscordOAuthClient>();
                services.AddSingleton<ConfigChangeNotifier>();
                services.AddSingleton<ISystemConfigService, SystemConfigService>();
                services.AddSingleton<IPeriodQuery, PeriodQuery>();
                services.AddSingleton<IPeriodRepository, PeriodRepository>();
                services.AddSingleton<IPlayerRepository, PlayerRepository>();
                services.AddSingleton<IPlayerService, PlayerService>();
                services.AddSingleton<IDiscordRoleMappingRepository, DiscordRoleMappingRepository>();
                services.ConfigureEventHandlers(b => b.AddEventHandlers<MemberUpdatedHandler>());
                
                services.AddMemoryCache();

                // 註冊自動執行的 Background Services
                services.AddHostedService<DiscordBotService>();       // Discord 啟動管理
                services.AddHostedService<DailyNotificationService>(); // 每日通知排程
                services.AddHostedService<RegistrationDeadlineJob>();  // 截止通知排程
                
                services.Configure<DiscordOptions>(
                    config.GetSection("Discord"));
                services.Configure<AppOptions>(
                    config.GetSection("App"));
            })
            .Build();

        await host.RunAsync(); // 啟動整個 Host，兩個 BackgroundService 都會自動啟動
    }
}