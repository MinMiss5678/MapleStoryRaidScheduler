using Dapper;
using Npgsql;
using Xunit;

namespace Test.Integration;

/// <summary>
/// 驗 migration 可逆：對一個乾淨 DB 依序套所有 up.sql，再反向套所有 down.sql。
/// 守 down.sql 正確性——down 寫錯（drop 不存在的欄位、順序錯）會在這裡爆，而不是上線 rollback 時才發現。
/// 用同一容器內的臨時 DB，避免動到共用 fixture 的 schema。
/// </summary>
[Collection("pg")]
[Trait("Category", "Integration")]
public class MigrationReversibilityIntegrationTests
{
    private readonly PostgresFixture _fx;
    public MigrationReversibilityIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task AllMigrations_UpThenDownInReverse_RunCleanly_AndDropAllTables()
    {
        var admin = _fx.ConnectionString;
        var dbName = $"mig_test_{Guid.NewGuid():N}";
        await ExecOnAsync(admin, $"CREATE DATABASE \"{dbName}\";");

        // Pooling=false：跑完不留連線在池裡，臨時 DB 才 drop 得掉
        var testCs = new NpgsqlConnectionStringBuilder(admin) { Database = dbName, Pooling = false }.ConnectionString;
        try
        {
            var dir = PostgresFixture.FindMigrationsDir();
            var ups = Directory.GetFiles(dir, "*.up.sql").OrderBy(f => f, StringComparer.Ordinal);
            var downs = Directory.GetFiles(dir, "*.down.sql").OrderByDescending(f => f, StringComparer.Ordinal);

            // 套所有 up（正向）
            foreach (var f in ups)
                await ExecOnAsync(testCs, await File.ReadAllTextAsync(f));

            // 套所有 down（反向）——任一 down.sql 有錯就會在此拋例外、測試紅
            foreach (var f in downs)
                await ExecOnAsync(testCs, await File.ReadAllTextAsync(f));

            // down 全跑完後，init.down 應已 drop 掉所有表 → public schema 歸零
            var remainingTables = await ScalarOnAsync(testCs, """
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = 'public' AND table_type = 'BASE TABLE';
                """);
            Assert.Equal(0, remainingTables);
        }
        finally
        {
            await ExecOnAsync(admin, $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE);");
        }
    }

    private static async Task ExecOnAsync(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        await c.ExecuteAsync(sql);
    }

    private static async Task<int> ScalarOnAsync(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        return await c.ExecuteScalarAsync<int>(sql);
    }
}
