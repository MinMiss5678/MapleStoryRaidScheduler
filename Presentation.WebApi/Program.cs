using System.Data;
using Application.Options;
using Dapper;
using Infrastructure.BackgroundJobs;
using Infrastructure.Dapper;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;
using Presentation.WebApi.Extensions;
using Presentation.WebApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices();
builder.Services.AddRepositories();
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
app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseMiddleware<AuthenticationMiddleware>();
app.UseMiddleware<UnitOfWorkMiddleware>();
app.MapControllers();
app.Run();