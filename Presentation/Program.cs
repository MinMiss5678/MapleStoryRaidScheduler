using System.Data;
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
using Serilog;
using StackExchange.Redis;

namespace Presentation;

public class Program
{
    static async Task Main()
    {
        var host = Host.CreateDefaultBuilder()
             .UseSerilog((ctx, services, config) =>
             {
                 // 跟 backend（Presentation.WebApi/Program.cs）同一套設定：Outbox 派發失敗/放棄這類
                 // 目前只印在 container console、進不了 Seq Alerting 的日誌，接上後才看得到、也才能被通知到。
                 var seqUrl = ctx.Configuration["Seq:ServerUrl"];
                 config
                     .ReadFrom.Configuration(ctx.Configuration)
                     .ReadFrom.Services(services)
                     .Enrich.FromLogContext()
                     .Enrich.WithProperty("Application", "MapleStoryRaidScheduler")
                     .WriteTo.Console()
                     .WriteTo.Seq(string.IsNullOrEmpty(seqUrl) ? "http://localhost:5341" : seqUrl);
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
                 else
                 {
                     var token = config["Discord:BotToken"]
                                 ?? throw new InvalidOperationException("Discord:BotToken 未設定");
                     var intents = DiscordIntents.AllUnprivileged |
                                   DiscordIntents.MessageContents |
                                   DiscordIntents.Guilds |
                                   DiscordIntents.GuildMembers;
                     services.AddDiscordClient(token, intents);
                 }

                 // 資料庫與 Repository 註冊。連線字串抽成區域變數 → OutboxDispatcher 也用同一份（自開專屬連線）。
                 var defaultConnectionFile = config["ConnectionStrings:DefaultConnectionFile"];
                 var connectionString = !string.IsNullOrEmpty(defaultConnectionFile) && File.Exists(defaultConnectionFile)
                     ? File.ReadAllText(defaultConnectionFile).Trim()
                     : config.GetConnectionString("DefaultConnection")!;
                 services.AddSingleton<IDbConnection>(_ =>
                 {
                     var conn = new NpgsqlConnection(connectionString);
                     conn.Open();
                     return conn;
                 });

                 services.AddSingleton<IUnitOfWork, UnitOfWork>();
                 services.AddSingleton<DbContext>();
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
                 services.ConfigureEventHandlers(b => b
                     .AddEventHandlers<MemberUpdatedHandler>()
                     .AddEventHandlers<MemberRemovedHandler>());

                 services.AddMemoryCache();

                 // Redis：session 快取跨 pod 共享。bot 的 MemberRemoved/Updated 撤 session 時，
                 // 刪的是共享快取 → API pod 立即失效（否則 bot 只清自己 pod、API 還留舊 session 到 TTL）。
                 // AbortOnConnectFail=false → Redis 掛不擋 bot 啟動；RedisSessionCache fail-open。
                 var redisConfiguration = config["Redis:ConfigurationFile"] is { } redisFile && File.Exists(redisFile)
                     ? File.ReadAllText(redisFile).Trim()
                     : config["Redis:Configuration"] ?? "localhost:6379";
                 var redisOptions = ConfigurationOptions.Parse(redisConfiguration);
                 redisOptions.AbortOnConnectFail = false;
                 services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
                 services.AddSingleton<ISessionCache, RedisSessionCache>();

                 // Outbox：bot 是消費端。寫入端（IOutbox）給 SystemConfigService 用；
                 // handler（ConfigChanged→喚醒 job）+ dispatcher 只在 bot（訂閱 notifier 的 job 在這）。
                 // dispatcher 自開專屬連線（不共用 singleton IDbConnection，免與 Discord 事件/計時器互踩）。
                 services.AddSingleton<IOutbox, Outbox>();
                 services.AddSingleton<IOutboxHandler, ConfigChangedOutboxHandler>();
                 services.AddHostedService(sp => new OutboxDispatcher(
                     connectionString,
                     sp.GetServices<IOutboxHandler>(),
                     sp.GetRequiredService<ILogger<OutboxDispatcher>>()));
                 services.AddHostedService(sp => new OutboxRetentionJob(
                     connectionString,
                     sp.GetRequiredService<ILogger<OutboxRetentionJob>>()));

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
