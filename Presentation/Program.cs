using System.Data;
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
using Presentation.Infrastructure.Discord.Handlers;
using Serilog;
using StackExchange.Redis;

namespace Presentation;

public class Program
{
    static async Task Main()
    {
        var host = Host.CreateDefaultBuilder()
             // 啟動即驗 DI 生命週期：ValidateScopes 禁止 root 解析 scoped、ValidateOnBuild 建構時就抓
             // captive dependency（singleton 誤吃 scoped）→ per-operation scope 的護欄，未來加註冊誤配立刻爆。
             .UseDefaultServiceProvider((_, options) =>
             {
                 options.ValidateScopes = true;
                 options.ValidateOnBuild = true;
             })
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

                 // 資料庫連線：連線字串集中到 IDbConnectionFactory（單一來源）→ scoped IDbConnection 與
                 // 背景 poller（OutboxDispatcher 等，各自 factory.Create() 自開專屬連線）共用同一份設定。
                 var defaultConnectionFile = config["ConnectionStrings:DefaultConnectionFile"];
                 var connectionString = !string.IsNullOrEmpty(defaultConnectionFile) && File.Exists(defaultConnectionFile)
                     ? File.ReadAllText(defaultConnectionFile).Trim()
                     : config.GetConnectionString("DefaultConnection")!;
                 services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(connectionString));
                 // 每個操作單元（Discord 事件 / outbox 消費 / 未來按鈕互動）拿專屬連線 → 並發安全，
                 // 與 API 端一致（Presentation.WebApi 也是 AddScoped<IDbConnection>）。DSharpPlus 每事件自動開 scope
                 // → 事件 handler（transient）直接注入 scoped 即可。連線延遲開啟（不 eager Open；DbContext.BeginAsync
                 // 或 Dapper 在需要時才開），避免每 scope 建構就佔一條連線。
                 services.AddScoped<IDbConnection>(sp => sp.GetRequiredService<IDbConnectionFactory>().Create());

                 // DB 鏈全 Scoped（每 scope 專屬 DbContext/連線/交易）：UoW、DbContext、repo、DB-touching service。
                 // 純 Discord/Redis（IDiscordService）維持 Singleton——不碰 DB、無 captive 風險。
                 services.AddScoped<IUnitOfWork, UnitOfWork>();
                 services.AddScoped<DbContext>();
                 services.AddSingleton<IDiscordService, DiscordService>();
                 services.AddScoped<ISessionService, SessionService>();
                 services.AddScoped<ISessionRepository, SessionRepository>();
                 services.AddScoped<ISessionQuery, SessionQuery>();
                 services.AddScoped<ISystemConfigService, SystemConfigService>();
                 services.AddScoped<IPlayerRepository, PlayerRepository>();
                 services.AddScoped<IPlayerService, PlayerService>();
                 services.AddScoped<IDiscordRoleMappingRepository, DiscordRoleMappingRepository>();

                 // leader-led：邀請按鈕互動（discord-inline-actions）走 TeamLeaderService.Accept/DeclineInviteAsync
                 // → bot 需註冊整個依賴圖（方案 B：直接複製那組 scoped，不共用 WebApi 的 extension）。
                 // 漏註冊由 ValidateOnBuild 啟動即抓（見 UseDefaultServiceProvider）。
                 services.AddScoped<IOutbox, Outbox>();
                 services.AddScoped<IBossRepository, BossRepository>();
                 services.AddScoped<ITeamSlotRepository, TeamSlotRepository>();
                 services.AddScoped<ITeamSlotCharacterRepository, TeamSlotCharacterRepository>();
                 services.AddScoped<ITeamSlotRequirementRepository, TeamSlotRequirementRepository>();
                 services.AddScoped<ILfgIntentRepository, LfgIntentRepository>();
                 services.AddScoped<ICharacterQuery, CharacterQuery>();
                 services.AddScoped<ITeamCandidateQuery, TeamCandidateQuery>();
                 services.AddScoped<ITeamMembershipQuery, TeamMembershipQuery>();
                 services.AddScoped<ITeamSlotEditLock, TeamSlotEditLock>();
                 services.AddScoped<ITeamLeaderService, TeamLeaderService>();

                 services.ConfigureEventHandlers(b => b
                     .AddEventHandlers<MemberUpdatedHandler>()
                     .AddEventHandlers<MemberRemovedHandler>()
                     .AddEventHandlers<TeamActionInteractionHandler>());

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

                 // Outbox：bot 是純消費端——dispatcher 輪詢已提交列、派給 handler 發 Discord DM。
                 // 寫入端（IOutbox.Enqueue）在 API（TeamLeaderService 發 TeamNotification）；bot 不 enqueue。
                 // dispatcher 自開專屬連線（不共用 singleton IDbConnection，免與 Discord 事件/計時器互踩）。
                 services.AddSingleton<IOutboxHandler, TeamNotificationOutboxHandler>();  // leader-led 組隊通知 → Discord DM
                 // 三個背景 poller 的相依（IDbConnectionFactory / IOutboxHandler / ILogger）皆可由 DI 解析
                 // → 直接型別註冊，免手動 new。各自仍 factory.Create() 自開專屬連線（不共用 scoped IDbConnection）。
                 services.AddHostedService<OutboxDispatcher>();
                 services.AddHostedService<OutboxRetentionJob>();
                 services.AddHostedService<LfgIntentCleanupJob>();

                 // 註冊自動執行的 Background Services
                 services.AddHostedService<DiscordBotService>();       // Discord 啟動管理

                 services.Configure<DiscordOptions>(
                     config.GetSection("Discord"));
                 services.Configure<AppOptions>(
                     config.GetSection("App"));
             })
             .Build();

        await host.RunAsync(); // 啟動整個 Host，兩個 BackgroundService 都會自動啟動
    }
}
