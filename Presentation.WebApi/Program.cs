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
builder.Services.AddScoped<ICharacterRegisterRepository, CharacterRegisterRepository>();
builder.Services.AddScoped<IPeriodService, PeriodService>();
builder.Services.AddScoped<IPeriodRepository, PeriodRepository>();
builder.Services.AddScoped<IPeriodQuery, PeriodQuery>();
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddScoped<IPlayerRegisterQuery, PlayerRegisterQuery>();
builder.Services.AddScoped<ITeamSlotService, TeamSlotService>();
builder.Services.AddScoped<ITeamSlotRepository, TeamSlotRepository>();
builder.Services.AddScoped<ITeamSlotQuery, TeamSlotQuery>();
builder.Services.AddScoped<ITeamSlotCharacterRepository, TeamSlotCharacterRepository>();
builder.Services.AddHostedService<WeeklyPeriodJob>();
builder.Services.AddScoped<NpgsqlConnection>(_ =>
    new NpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddControllers();

// Dapper TimeOnly support
SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"));

builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection("Jwt"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<AuthenticationMiddleware>();
var options = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};

// 允許 Docker bridge network
options.KnownNetworks.Clear(); // 清掉預設 127.0.0.1/8
options.KnownProxies.Clear();
app.UseForwardedHeaders(options);
app.UseHttpsRedirection();
app.MapControllers();
app.Run();