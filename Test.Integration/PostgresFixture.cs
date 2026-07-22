using Dapper;
using Infrastructure.Dapper;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 起一顆拋棄式 PostgreSQL 容器，套用 db/migrations 的 up.sql 建 schema。
/// 一個 collection 共用一顆容器（容器啟動慢），測試間用 ResetAsync 清資料。
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    // Testcontainers 的無參數 PostgreSqlBuilder() 被標為 obsolete，但這是官方文件的標準用法
    // （image 由 .WithImage 指定）；暫窄範圍抑制，待套件提供乾淨替代 API 再改。
#pragma warning disable CS0618
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:18")
        .Build();
#pragma warning restore CS0618

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        // 自訂 TimeOnly handler（PlayerAvailability 的 time 欄位會用到）
        SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
        await _container.StartAsync();
        await ApplyMigrationsAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>建立指向測試容器的 DbContext（Dapper 會自動開連線）。</summary>
    public DbContext CreateDbContext() => new(new NpgsqlConnection(ConnectionString));

    /// <summary>測試間清空所有資料、保留 schema。</summary>
    public async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            TRUNCATE "TeamSlotCharacter","TeamSlot","CharacterRegister","PlayerAvailability",
                     "PlayerRegister","Character","Player","BossTemplateRequirement","BossTemplate",
                     "Boss","Period","JobCategory","DiscordRoleMapping","Session","SystemConfig"
            RESTART IDENTITY CASCADE;
            """);
    }

    private async Task ApplyMigrationsAsync()
    {
        var dir = FindMigrationsDir();
        var ups = Directory.GetFiles(dir, "*.up.sql").OrderBy(f => f, StringComparer.Ordinal);
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        foreach (var file in ups)
            await conn.ExecuteAsync(await File.ReadAllTextAsync(file));
    }

    // 從測試輸出目錄往上找 repo 根的 db/migrations（migration 可逆測試也共用）
    public static string FindMigrationsDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "db", "migrations")))
            d = d.Parent;
        if (d is null)
            throw new DirectoryNotFoundException($"找不到 db/migrations（從 {AppContext.BaseDirectory} 往上找）");
        return Path.Combine(d.FullName, "db", "migrations");
    }
}

[CollectionDefinition("pg")]
public class PgCollection : ICollectionFixture<PostgresFixture>;
