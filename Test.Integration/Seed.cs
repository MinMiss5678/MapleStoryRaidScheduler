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

    /// <summary>在指定隊伍塞一個空位（CharacterId 為 null）。</summary>
    public static async Task EmptySlotAsync(string cs, int teamSlotId)
    {
        await using var c = await OpenAsync(cs);
        await c.ExecuteAsync(
            """INSERT INTO "TeamSlotCharacter"("TeamSlotId","Job") VALUES (@teamSlotId,'-');""",
            new { teamSlotId });
    }

    public static async Task<int> CountTeamAsync(string cs, int teamSlotId)
    {
        await using var c = await OpenAsync(cs);
        return await c.ExecuteScalarAsync<int>(
            """SELECT COUNT(*) FROM "TeamSlot" WHERE "Id" = @teamSlotId;""", new { teamSlotId });
    }

}
