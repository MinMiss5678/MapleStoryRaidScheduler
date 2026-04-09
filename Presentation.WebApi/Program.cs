using System.Data;
using Application.Events;
using Application.Interface;
using Application.Options;
using Application.Queries;
using Application.Services;
using Dapper;
using Domain.Repositories;
using Infrastructure.BackgroundJobs;
using Infrastructure.Dapper;
using Infrastructure.Query;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;
using Presentation.WebApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddScoped<AuthenticationMiddleware>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<DbContext>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<AuthAppService, AuthAppService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();
builder.Services.AddScoped<ISessionQuery, SessionQuery>();
builder.Services.AddScoped<IPlayerService, PlayerService>();
builder.Services.AddScoped<IPlayerRepository, PlayerRepository>();
builder.Services.AddScoped<ICharacterQuery, CharacterQuery>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<ICharacterService, CharacterService>();
builder.Services.AddScoped<ICharacterRepository, CharacterRepository>();
builder.Services.AddScoped<IDiscordOAuthClient, DiscordOAuthClient>();
builder.Services.AddScoped<IBossService, BossService>();
builder.Services.AddScoped<IBossRepository, BossRepository>();
builder.Services.AddScoped<IRegisterService, RegisterService>();
builder.Services.AddScoped<IPlayerRegisterRepository, PlayerRegisterRepository>();
builder.Services.AddScoped<IPlayerAvailabilityRepository, PlayerAvailabilityRepository>();
builder.Services.AddScoped<ICharacterRegisterRepository, CharacterRegisterRepository>();
builder.Services.AddScoped<IPeriodService, PeriodService>();
builder.Services.AddScoped<IPeriodRepository, PeriodRepository>();
builder.Services.AddScoped<IPeriodQuery, PeriodQuery>();
builder.Services.AddScoped<ISystemConfigService, SystemConfigService>();
builder.Services.AddScoped<IDiscordRoleMappingRepository, DiscordRoleMappingRepository>();
builder.Services.AddScoped<IJobCategoryRepository, JobCategoryRepository>();
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddScoped<IPlayerRegisterQuery, PlayerRegisterQuery>();
builder.Services.AddScoped<ITeamSlotService, TeamSlotService>();
builder.Services.AddScoped<ITeamSlotAutoAssignService, TeamSlotAutoAssignService>();
builder.Services.AddScoped<ITeamSlotMergeService, TeamSlotMergeService>();
builder.Services.AddScoped<ITeamSlotRepository, TeamSlotRepository>();
builder.Services.AddScoped<ITeamSlotQuery, TeamSlotQuery>();
builder.Services.AddScoped<ITeamSlotCharacterService, TeamSlotCharacterService>();
builder.Services.AddScoped<ITeamSlotCharacterRepository, TeamSlotCharacterRepository>();
builder.Services.AddSingleton<ConfigChangeNotifier>();
builder.Services.AddHostedService<RegistrationDeadlineJob>();
builder.Services.AddHostedService<WeeklyPeriodJob>();

var defaultConnectionFile = builder.Configuration.GetConnectionString("DefaultConnectionFile");
if (!string.IsNullOrEmpty(defaultConnectionFile) && File.Exists(defaultConnectionFile))
{
    var defaultConnection = File.ReadAllText(defaultConnectionFile).Trim();
    builder.Services.AddScoped<IDbConnection>(_ =>
    {
        var conn = new NpgsqlConnection(defaultConnection);
        conn.Open();
        return conn;
    });
}
else
{
    builder.Services.AddScoped<IDbConnection>(_ =>
    {
        var conn = new NpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection"));
        conn.Open();
        return conn;
    });
}

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        // 將所有 long/ulong 序列化為 string 以避免 JavaScript 精度遺失
        options.SerializerSettings.Converters.Add(new Utils.JsonConverters.BigIntStringConverter());
    });

// Dapper TimeOnly support
SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .PostConfigure(options =>
    {
        if (!string.IsNullOrEmpty(options.SecretKeyFile) &&
            File.Exists(options.SecretKeyFile))
        {
            options.SecretKey =
                File.ReadAllText(options.SecretKeyFile).Trim();
        }
    });

builder.Services.AddOptions<DiscordOptions>()
    .Bind(builder.Configuration.GetSection("Discord"))
    .PostConfigure(options =>
    {
        if (!string.IsNullOrEmpty(options.BotTokenFile) &&
            File.Exists(options.BotTokenFile))
        {
            options.BotToken =
                File.ReadAllText(options.BotTokenFile).Trim();
        }
        
        if (!string.IsNullOrEmpty(options.ClientSecretFile) &&
            File.Exists(options.ClientSecretFile))
        {
            options.ClientSecret =
                File.ReadAllText(options.ClientSecretFile).Trim();
        }
        else
        {
            options.ClientSecret = options.ClientSecret;
        }
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var options = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};

// 允許 Docker bridge network
options.KnownNetworks.Clear(); // 清掉預設 127.0.0.1/8
options.KnownProxies.Clear();
app.UseForwardedHeaders(options);
app.UseHttpsRedirection();
app.UseMiddleware<AuthenticationMiddleware>();
app.UseMiddleware<UnitOfWorkMiddleware>();
app.MapControllers();
app.Run();