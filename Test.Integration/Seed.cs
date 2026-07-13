using Dapper;
using Npgsql;

namespace Test.Integration;

/// <summary>整合測試用的原生 SQL 播種工具（處理 FK 前置資料）。</summary>
internal static class Seed
{
    private static async Task<NpgsqlConnection> OpenAsync(string cs)
    {
        var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        return c;
    }

    public static async Task<int> BossAsync(string cs, string name = "B", int requireMembers = 6)
    {
        await using var c = await OpenAsync(cs);
        return await c.ExecuteScalarAsync<int>(
            """INSERT INTO "Boss"("Name","RequireMembers","RoundConsumption") VALUES (@name,@rm,1) RETURNING "Id";""",
            new { name, rm = requireMembers });
    }

    public static async Task<int> PeriodAsync(string cs, DateTimeOffset start, DateTimeOffset end)
    {
        await using var c = await OpenAsync(cs);
        return await c.ExecuteScalarAsync<int>(
            """INSERT INTO "Period"("StartDate","EndDate") VALUES (@start,@end) RETURNING "Id";""",
            new { start, end });
    }

    public static async Task<int> TeamSlotAsync(string cs, int bossId, string source, DateTimeOffset? slot = null)
    {
        await using var c = await OpenAsync(cs);
        return await c.ExecuteScalarAsync<int>(
            """INSERT INTO "TeamSlot"("BossId","SlotDateTime","Source") VALUES (@bossId,@slot,@source) RETURNING "Id";""",
            new { bossId, slot = slot ?? new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero), source });
    }

    public static async Task PlayerAsync(string cs, long discordId, string name, string role = "user")
    {
        await using var c = await OpenAsync(cs);
        await c.ExecuteAsync(
            """INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (@discordId,@name,@role);""",
            new { discordId, name, role });
    }

    public static async Task CharacterAsync(string cs, string id, long discordId, string name, string job, int atk)
    {
        await using var c = await OpenAsync(cs);
        await c.ExecuteAsync(
            """INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower") VALUES (@id,@discordId,@name,@job,@atk);""",
            new { id, discordId, name, job, atk });
    }

    /// <summary>在指定隊伍塞一個「有角色」的成員（含 Player+Character），回傳該 TeamSlotCharacter 的 Id。</summary>
    public static async Task<int> OccupiedSlotAsync(string cs, int teamSlotId, long discordId = 111, string charId = "occ1")
    {
        await PlayerAsync(cs, discordId, "P");
        await CharacterAsync(cs, charId, discordId, "C", "Warrior", 1000);
        await using var c = await OpenAsync(cs);
        return await c.ExecuteScalarAsync<int>(
            """
            INSERT INTO "TeamSlotCharacter"("TeamSlotId","DiscordId","CharacterId","Job")
            VALUES (@teamSlotId,@discordId,@charId,'Warrior') RETURNING "Id";
            """,
            new { teamSlotId, discordId, charId });
    }

    public static async Task<int> CountTeamAsync(string cs, int teamSlotId)
    {
        await using var c = await OpenAsync(cs);
        return await c.ExecuteScalarAsync<int>(
            """SELECT COUNT(*) FROM "TeamSlot" WHERE "Id" = @teamSlotId;""", new { teamSlotId });
    }

    public static async Task<int> PlayerRegisterAsync(string cs, long discordId, int periodId)
    {
        await using var c = await OpenAsync(cs);
        return await c.ExecuteScalarAsync<int>(
            """INSERT INTO "PlayerRegister"("DiscordId","PeriodId") VALUES (@discordId,@periodId) RETURNING "Id";""",
            new { discordId, periodId });
    }

    public static async Task CharacterRegisterAsync(string cs, int playerRegisterId, string charId, int bossId, int rounds)
    {
        await using var c = await OpenAsync(cs);
        await c.ExecuteAsync(
            """
            INSERT INTO "CharacterRegister"("PlayerRegisterId","CharacterId","BossId","Rounds")
            VALUES (@playerRegisterId,@charId,@bossId,@rounds);
            """,
            new { playerRegisterId, charId, bossId, rounds });
    }

    public static async Task AvailabilityAsync(string cs, int playerRegisterId, int weekday, TimeOnly start, TimeOnly end)
    {
        await using var c = await OpenAsync(cs);
        await c.ExecuteAsync(
            """
            INSERT INTO "PlayerAvailability"("PlayerRegisterId","Weekday","StartTime","EndTime")
            VALUES (@playerRegisterId,@weekday,@start,@end);
            """,
            new { playerRegisterId, weekday, start, end });
    }
}
