using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Application.Interface;
using Application.Options;
using Dapper;
using Infrastructure.BackgroundJobs;
using Infrastructure.Dapper;
using Infrastructure.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;
using Presentation.WebApi.Extensions;
using Presentation.WebApi.HealthChecks;
using Presentation.WebApi.Middleware;
using Presentation.WebApi.RateLimiting;
using Serilog;
using Serilog.Events;

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

    // sentry.io（免費版，見 plans/2026-08-03-bugsink-integration.md）：
    // 只補 Seq 沒有的 stack-trace 去重 + resolved/unresolved，職責不重疊，兩個 sink 並存。
    // 沒設 Dsn（本機開發預設）就不掛這個 sink，不會因為缺設定噴例外。
    var sentryDsnFile = ctx.Configuration["Sentry:DsnFile"];
    var sentryDsn = !string.IsNullOrEmpty(sentryDsnFile) && File.Exists(sentryDsnFile)
        ? File.ReadAllText(sentryDsnFile).Trim()
        : ctx.Configuration["Sentry:Dsn"];
    if (!string.IsNullOrEmpty(sentryDsn) && ctx.HostingEnvironment.IsProduction())
    {
        // DiscordId 雜湊密鑰：沒設就不送 DiscordId tag（寧可少一個可搜尋欄位，不送明碼 ID 出去）。
        var discordIdHashKeyFile = ctx.Configuration["Sentry:DiscordIdHashKeyFile"];
        var discordIdHashKey = !string.IsNullOrEmpty(discordIdHashKeyFile) && File.Exists(discordIdHashKeyFile)
            ? File.ReadAllText(discordIdHashKeyFile).Trim()
            : ctx.Configuration["Sentry:DiscordIdHashKey"];

        config.WriteTo.Sentry(o =>
        {
            o.Dsn = sentryDsn;
            o.MinimumEventLevel = LogEventLevel.Error;
            o.MinimumBreadcrumbLevel = LogEventLevel.Warning;
            o.TracesSampleRate = 0; // 不需要 performance tracing（YAGNI）
            o.SetBeforeSend(sentryEvent =>
            {
                // DiscordId 是間接識別個人的資料，不能明碼送到第三方——用 HMAC-SHA256 雜湊過再轉 tag。
                // 帶密鑰是關鍵：Discord snowflake ID 是結構化數字（時間戳+worker+序號），範圍可列舉，
                // 沒有密鑰的雜湊（純 SHA256）等於沒雜湊；HMAC 沒密鑰就連候選值都算不出來。
                if (sentryEvent.Extra.TryGetValue("DiscordId", out var discordId) && discordId is not null)
                {
                    if (!string.IsNullOrEmpty(discordIdHashKey))
                    {
                        var hash = Convert.ToHexString(HMACSHA256.HashData(
                            Encoding.UTF8.GetBytes(discordIdHashKey), Encoding.UTF8.GetBytes(discordId.ToString()!)));
                        sentryEvent.SetTag("discord_id_hash", hash);
                    }
                    // 不管有沒有雜湊成功（例如密鑰沒設），Extra 裡的明碼都要清掉——
                    // SetTag 只是「多加」一個 tag，原本的 Extra["DiscordId"] 明碼不會自動消失。
                    sentryEvent.SetExtra("DiscordId", "[Filtered]");
                }

                // 部分例外（如 Npgsql 連線失敗）會把連線字串/token 明碼塞進 Message，送出前擋掉
                foreach (var sentryException in sentryEvent.SentryExceptions ?? [])
                    sentryException.Value = ScrubSensitive(sentryException.Value, discordIdHashKey);

                // sentryEvent.Message 是「log 訊息本身」渲染後的結果（例如 LogError(ex, "user {DiscordId}", id)
                // 裡的 "user {DiscordId}" 部分），跟上面 SentryExceptions[].Value（.NET Exception.Message）
                // 是完全分開的欄位——只掃 SentryExceptions 掃不到這裡，DiscordId/IP/token 一樣可能夾在這段文字裡。
                if (sentryEvent.Message is { } message)
                {
                    message.Formatted = ScrubSensitive(message.Formatted, discordIdHashKey);
                    // Params 是渲染前的原始參數（例如上面那個 DiscordId 的原始 ulong），
                    // Formatted 掃過了字串，但 Sentry 網頁的「JSON」原始檢視還是讀得到 Params 裡的明碼——兩邊都要清。
                    message.Params = null;
                }

                return sentryEvent;
            });
            // Warning 等級的 log（業務例外、限流 fail-open 等）會變成 breadcrumb 夾帶送出，
            // 跟主要例外訊息是分開的管線,BeforeSend 掃不到,要用專屬的 BeforeBreadcrumb hook。
            // 這條路徑才是 DiscordId 實際外洩的地方：codebase 裡帶 {DiscordId} 的 log 呼叫全部是
            // LogWarning（RedisSessionCache/RedisFixedWindowRateLimiter/限流觸發),沒有 LogError,
            // 上面 BeforeSend 那段雜湊只作用在 Extra（Error 等級才有),保護不到這裡——
            // 一定要在 ScrubSensitive 裡也雜湊裸露的 Discord ID,不能只靠 BeforeSend。
            // Breadcrumb.Message 對外是唯讀,只能整個重建一個新的回傳。
            o.SetBeforeBreadcrumb(breadcrumb => new Breadcrumb(
                message: ScrubSensitive(breadcrumb.Message, discordIdHashKey) ?? "",
                type: breadcrumb.Type ?? "default",
                data: breadcrumb.Data,
                category: breadcrumb.Category,
                level: breadcrumb.Level));
        });
    }
});

static string? ScrubSensitive(string? message, string? discordIdHashKey)
{
    if (string.IsNullOrEmpty(message)) return message;
    message = Regex.Replace(message, "(?i)(password|pwd)\\s*=\\s*[^;]+", "$1=[Filtered]");
    message = Regex.Replace(message, @"\beyJ[\w-]+\.[\w-]+\.[\w-]+\b", "[Filtered JWT]");
    // JSON 格式的 token 欄位（OAuth 回應解析失敗時，例外訊息可能夾帶原始內容片段）——
    // Discord/MS 的 token 不是 JWT 格式，上面那條正則抓不到，這裡照 key 名稱單獨擋。
    message = Regex.Replace(message, "(?i)\"(access_token|refresh_token|token|bot_token|api_key)\"\\s*:\\s*\"[^\"]*\"", "\"$1\":\"[Filtered]\"");
    // IPv4 位址（例如限流觸發時記錄的真實 client IP）
    message = Regex.Replace(message, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", "[Filtered IP]");
    // Discord snowflake ID（17-19 位數字，例如 discordId=836636705498595340）——
    // 跟 BeforeSend 那段 Extra["DiscordId"] 走同一把 HMAC key 雜湊，不是單純 [Filtered]，
    // 沒密鑰就退化成 [Filtered]，跟 tag 那段的「沒密鑰不送明碼」邏輯一致。
    message = Regex.Replace(message, @"\b\d{17,19}\b", m =>
        string.IsNullOrEmpty(discordIdHashKey)
            ? "[Filtered]"
            : Convert.ToHexString(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(discordIdHashKey), Encoding.UTF8.GetBytes(m.Value))));
    return message;
}

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

// 不在建構時 Open（避免「解析即 I/O」的副作用）：
//   - 讀取：Dapper 對關閉的連線會自動開/關，不需先 Open。
//   - 寫入：UnitOfWork 的 DbContext.Begin() 用到交易時才 Open（BeginTransaction 需連線已開）。
// 好處：注入連線鏈的元件（如 AuthenticationMiddleware）建構時不再碰 DB → 可獨立測試、
//       DB 掛掉退化成乾淨的查詢失敗而非建構失敗。
builder.Services.AddScoped<IDbConnection>(_ => new NpgsqlConnection(connectionString));

// 健康檢查：readiness 探針查 DB（tag "ready"）；liveness 不掛任何 check（見下方 endpoint）
builder.Services.AddHealthChecks()
    .AddCheck("database", new DatabaseHealthCheck(connectionString), tags: new[] { "ready" });

builder.Services.AddMemoryCache();

// Redis：重複提交防護的跨 pod 去重儲存（取代 IdempotencyMiddleware 原本的 per-pod IMemoryCache）。
// AbortOnConnectFail=false → Redis 掛也不擋 app 啟動，搭配 RedisIdempotencyStore 的 fail-open：
// Redis 不可用時放行 + 記 log，不因去重快取抖動擋掉寫入。連線字串走 Redis:Configuration（可用 *File 覆寫）。
var redisConfiguration = builder.Configuration["Redis:ConfigurationFile"] is { } redisFile && File.Exists(redisFile)
    ? File.ReadAllText(redisFile).Trim()
    : builder.Configuration["Redis:Configuration"] ?? "localhost:6379";
var redisOptions = ConfigurationOptions.Parse(redisConfiguration);
redisOptions.AbortOnConnectFail = false;
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
// session 快取也走 Redis（跨 pod 共享）→ 登出／強制下線的撤銷一次刪除即在所有 pod 生效（取代 per-pod IMemoryCache）
builder.Services.AddSingleton<ISessionCache, RedisSessionCache>();
builder.Services.AddHttpClient();

// 登入後「按身分」限流：以驗證過的 discordId（session/JWT，client 偽造不了）當 partition key，
// 換 IP 也繞不掉。未登入請求不在此限（登入前的暴力破解另案處理，需 IP/CAPTCHA/帳號鎖定）。
// middleware 掛在 Auth 之後、UnitOfWork 之前 → 被擋的請求不會白開 DB 交易。
// 綁 RateLimitOptions 到 DI（延遲繫結）→ 值在請求處理時才讀，才吃得到測試 / 環境覆寫的設定
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("RateLimit"));
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    rateLimiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var discordId = httpContext.User.FindFirst("discordId")?.Value;

        // 未登入 → 不限流（本階段只做「登入後按身分」）
        if (string.IsNullOrEmpty(discordId))
            return RateLimitPartition.GetNoLimiter("anonymous");

        // 每個使用者固定視窗（預設 100 次 / 10 秒）——正常瀏覽（每頁數個 React Query）遠不會碰到，
        // 但擋得住腳本狂打。上限由 appsettings 的 RateLimit 區段控制（測試會調小以驗行為）。
        // 計數存 Redis（RedisFixedWindowRateLimiter）→ 跨 pod 共用同一上限，非 per-pod 各算各的。
        var rl = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        var rlRedis = httpContext.RequestServices.GetRequiredService<IConnectionMultiplexer>();
        var rlLogger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger<RedisFixedWindowRateLimiter>();
        return RateLimitPartition.Get(discordId, key => new RedisFixedWindowRateLimiter(
            rlRedis, rlLogger, $"ratelimit:{key}", rl.PermitLimit, TimeSpan.FromSeconds(rl.WindowSeconds)));
    });

    // 觸發時記 log（配 Serilog/Seq 可觀測），並回傳 Retry-After 提示
    rateLimiterOptions.OnRejected = (context, _) =>
    {
        var discordId = context.HttpContext.User.FindFirst("discordId")?.Value ?? "unknown";
        // 真實 client IP 由前端 proxy 覆寫、UseForwardedHeaders 還原（見 ForwardedHeaders 設定）
        var clientIp = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        Log.Warning("Rate limit 觸發：discordId={DiscordId} ip={ClientIp} path={Path}",
            discordId, clientIp, context.HttpContext.Request.Path);
        context.HttpContext.Response.Headers["Retry-After"] = "10";
        return ValueTask.CompletedTask;
    };
});
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

builder.Services.AddOptions<MicrosoftMailOptions>()
    .Bind(builder.Configuration.GetSection("MicrosoftMail"))
    .PostConfigure(options =>
    {
        if (!string.IsNullOrEmpty(options.RefreshTokenFile) &&
            File.Exists(options.RefreshTokenFile))
        {
            options.RefreshToken =
                File.ReadAllText(options.RefreshTokenFile).Trim();
        }

        if (!string.IsNullOrEmpty(options.WebhookSecretFile) &&
            File.Exists(options.WebhookSecretFile))
        {
            options.WebhookSecret =
                File.ReadAllText(options.WebhookSecretFile).Trim();
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
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1 // 只信最後一跳（前端 proxy）設的那一筆 X-Forwarded-For
};

// backend 只在叢集內部可達（非公開）；前端 proxy 已刪掉 client 可偽造的 header、
// 改設 cf-connecting-ip 的真 IP。這裡信任「內部私有網段」送來的 forwarded header
// → 取得真實 client IP 且不可偽造（公網無法從私有 IP 連到 backend）。
options.KnownNetworks.Clear();
options.KnownProxies.Clear();
options.KnownNetworks.Add(new IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8));
options.KnownNetworks.Add(new IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12));
options.KnownNetworks.Add(new IPNetwork(System.Net.IPAddress.Parse("192.168.0.0"), 16));
app.UseForwardedHeaders(options);

// 健康檢查放最前面（auth/uow/serilog 之前）當「終端中介軟體」短路——完全繞過後面整條管線。
// 探針不該需要認證、也不該開交易；readiness 的 DB 檢查走專屬 HealthCheck（DatabaseHealthCheck），
// 不經請求管線 → DB 掛掉時乾淨回 503，liveness 不查 DB 故不會誤判重啟 pod。
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
// 限流放 Auth 之後（才有 discordId 可分區）、UoW 之前（被擋的請求不白開 DB 交易）
app.UseRateLimiter();
app.UseMiddleware<UnitOfWorkMiddleware>();
app.MapControllers();
app.Run();

// 讓整合測試（WebApplicationFactory<Program>）能參照到 top-level Program 類別
public partial class Program;
