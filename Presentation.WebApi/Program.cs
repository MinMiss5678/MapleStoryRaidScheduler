using System.Data;
using Application.Options;
using Dapper;
using Infrastructure.BackgroundJobs;
using Infrastructure.Dapper;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;
using Presentation.WebApi.Extensions;
using Presentation.WebApi.HealthChecks;
using Presentation.WebApi.Middleware;
using Serilog;

// 最早初始化 Serilog，讓 startup 期間的錯誤也能被記錄
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, config) =>
{
    var seqUrl = ctx.Configuration["Seq:ServerUrl"];
    config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "MapleStoryRaidScheduler")
        .WriteTo.Console()
        .WriteTo.Seq(string.IsNullOrEmpty(seqUrl) ? "http://localhost:5341" : seqUrl);
});

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices();
builder.Services.AddRepositories();
builder.Services.AddHostedService<WeeklyPeriodJob>();

var defaultConnectionFile = builder.Configuration.GetConnectionString("DefaultConnectionFile");
var connectionString = !string.IsNullOrEmpty(defaultConnectionFile) && File.Exists(defaultConnectionFile)
    ? File.ReadAllText(defaultConnectionFile).Trim()
    : builder.Configuration.GetConnectionString("DefaultConnection")!;

builder.Services.AddScoped<IDbConnection>(_ =>
{
    var conn = new NpgsqlConnection(connectionString);
    conn.Open();
    return conn;
});

// 健康檢查：readiness 探針查 DB（tag "ready"）；liveness 不掛任何 check（見下方 endpoint）
builder.Services.AddHealthChecks()
    .AddCheck("database", new DatabaseHealthCheck(connectionString), tags: new[] { "ready" });

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

// 健康檢查放最前面（auth/uow/serilog 之前）當「終端中介軟體」短路——必須完全繞過後面整條管線。
// 原因：AuthenticationMiddleware 的相依鏈（session/player service → repo → IDbConnection）
// 建構時就 eager 開 DB 連線；若讓 health 走到它，DB 掛掉時 readiness 會因「建不出 auth 中介軟體」
// 回 500 而非乾淨 503，且 liveness 會誤判（DB 掛 → pod 被重啟）。放這裡完全避開。
// 放在 UseHttpsRedirection 之前 → 內部 HTTP 探針不會被 307 轉址。
app.UseHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // liveness：app 活著就 200，不查 DB
});
app.UseHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready") // readiness：查 DB，DB 沒好回 503
});

app.UseHttpsRedirection();
// 記錄每個 HTTP Request / Response（不含健康檢查等靜態路徑）
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();
app.UseMiddleware<AuthenticationMiddleware>();
app.UseMiddleware<UnitOfWorkMiddleware>();
app.MapControllers();
app.Run();