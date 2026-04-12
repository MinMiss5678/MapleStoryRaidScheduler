using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Migrations;

/// <summary>
/// 提供給 dotnet ef 工具使用的 design-time factory。
/// 連線字串優先讀取環境變數 MIGRATION_DB_URL，否則使用本機預設值。
/// </summary>
public class MigrationDbContextFactory : IDesignTimeDbContextFactory<MigrationDbContext>
{
    public MigrationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MIGRATION_DB_URL")
            ?? "Host=localhost;Port=5432;Database=presentationdb;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<MigrationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new MigrationDbContext(options);
    }
}
