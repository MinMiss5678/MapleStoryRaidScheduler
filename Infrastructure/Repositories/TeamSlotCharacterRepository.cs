using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class TeamSlotCharacterRepository : ITeamSlotCharacterRepository
{
    private readonly DbContext _dbContext;

    public TeamSlotCharacterRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CreateAsync(TeamSlotCharacter teamSlot)
    {
        await _dbContext.Repository<TeamSlotCharacterDbModel>().InsertAsync(new TeamSlotCharacterDbModel
        {
            TeamSlotId = teamSlot.TeamSlotId,
            DiscordId = (long)teamSlot.DiscordId,
            DiscordName = teamSlot.DiscordName,
            CharacterId = teamSlot.CharacterId,
            CharacterName = teamSlot.CharacterName,
            Job = teamSlot.Job,
            AttackPower = teamSlot.AttackPower,
            Rounds = teamSlot.Rounds,
            IsManual = teamSlot.IsManual,
            // leader-led：舊路徑（fill/auto-assign）不設 → 預設 Confirmed / SlotDateTime=null（不變行為）；
            // leader 邀請設 Invited + 隊時間（跨隊重疊 unique 用）。
            Status = teamSlot.Status,
            SlotDateTime = teamSlot.SlotDateTime
        });
    }

    /// <summary>某隊已 Confirmed 的真實成員數（排除 vacancy 哨兵 DiscordId=0）——leader accept 的容量把關。</summary>
    public async Task<int> CountConfirmedAsync(int teamSlotId)
    {
        const string sql = """
            SELECT COUNT(*) FROM "TeamSlotCharacter"
            WHERE "TeamSlotId" = @teamSlotId AND "Status" = 'Confirmed' AND "DiscordId" <> 0;
            """;
        return (await _dbContext.QueryAsync<int>(sql, new { teamSlotId })).FirstOrDefault();
    }

    /// <summary>取單一成員列（含 xmin 版本），供 accept/decline 狀態轉移。</summary>
    public async Task<TeamSlotCharacter?> GetByIdAsync(int id)
    {
        const string sql = """
            SELECT "Id" AS "Id", "TeamSlotId" AS "TeamSlotId", "DiscordId" AS "DiscordId",
                   "DiscordName" AS "DiscordName", "CharacterId" AS "CharacterId", "Job" AS "Job",
                   "Status" AS "Status", xmin::text AS "Version"
            FROM "TeamSlotCharacter" WHERE "Id" = @id;
            """;
        var row = (await _dbContext.QueryAsync<MemberRow>(sql, new { id })).FirstOrDefault();
        if (row == null) return null;
        return new TeamSlotCharacter
        {
            Id = row.Id,
            TeamSlotId = row.TeamSlotId,
            DiscordId = (ulong)row.DiscordId,
            DiscordName = row.DiscordName,
            CharacterId = row.CharacterId,
            Job = row.Job,
            Status = row.Status,
            Version = row.Version
        };
    }

    /// <summary>樂觀鎖（xmin）改狀態——accept（Invited→Confirmed）/ decline（→Rejected）。false = 版本對不上。</summary>
    public async Task<bool> UpdateStatusAsync(int id, string status, string version)
    {
        int? memberId = id;  // 用 int? 區域變數，避免 int→int? 的 Convert 節點讓 SqlExpressionVisitor 掛掉
        var sql = new UpdateBuilder<TeamSlotCharacterDbModel>()
            .Set(x => x.Status, status)
            .Where(x => x.Id == memberId)
            .WhereRaw("xmin = @version::xid", new { version });
        return await _dbContext.ExecuteAsync(sql) > 0;
    }

    public async Task<TeamSlotCharacter?> GetConfirmedMemberAsync(int teamSlotId, ulong discordId)
    {
        const string sql = """
            SELECT "Id" AS "Id", "TeamSlotId" AS "TeamSlotId", "DiscordId" AS "DiscordId",
                   "DiscordName" AS "DiscordName", "CharacterId" AS "CharacterId", "Job" AS "Job",
                   "Status" AS "Status", xmin::text AS "Version"
            FROM "TeamSlotCharacter"
            WHERE "TeamSlotId" = @teamSlotId AND "DiscordId" = @discordId AND "Status" = 'Confirmed'
            LIMIT 1;
            """;
        var row = (await _dbContext.QueryAsync<MemberRow>(sql, new { teamSlotId, discordId = (long)discordId })).FirstOrDefault();
        if (row == null) return null;
        return new TeamSlotCharacter
        {
            Id = row.Id,
            TeamSlotId = row.TeamSlotId,
            DiscordId = (ulong)row.DiscordId,
            DiscordName = row.DiscordName,
            CharacterId = row.CharacterId,
            Job = row.Job,
            Status = row.Status,
            Version = row.Version
        };
    }

    public async Task<bool> LeaveAsync(int id, string version)
    {
        // Confirmed→Left + 記 LeftAt=now()，xmin 樂觀鎖。退隊只減 Confirmed 數、不觸容量上限 → 不需 advisory lock。
        const string sql = """
            UPDATE "TeamSlotCharacter" SET "Status" = 'Left', "LeftAt" = now()
            WHERE "Id" = @id AND xmin = @version::xid;
            """;
        return await _dbContext.ExecuteAsync(sql, new { id, version }) > 0;
    }

    public async Task<IReadOnlyCollection<ulong>> GetActiveMemberDiscordIdsAsync(int teamSlotId)
    {
        // active = Confirmed/Invited/Applied（占位或待處理）；Rejected/Left 排除 → 拒絕/退隊過的可重新出現在候選、可重邀。
        const string sql = """
            SELECT DISTINCT "DiscordId" FROM "TeamSlotCharacter"
            WHERE "TeamSlotId" = @teamSlotId AND "Status" IN ('Confirmed', 'Invited', 'Applied');
            """;
        var ids = await _dbContext.QueryAsync<long>(sql, new { teamSlotId });
        return ids.Select(x => (ulong)x).ToHashSet();
    }

    public async Task<IReadOnlyCollection<ulong>> GetConfirmedDiscordIdsAtAsync(DateTimeOffset slotDateTime)
    {
        // 已在該精確開團時刻別隊 Confirmed 者（對齊 uq_tsc_confirmed_overlap）→ 候選排除「不可分身」。
        const string sql = """
            SELECT DISTINCT "DiscordId" FROM "TeamSlotCharacter"
            WHERE "Status" = 'Confirmed' AND "SlotDateTime" = @slot AND "DiscordId" <> 0;
            """;
        var ids = await _dbContext.QueryAsync<long>(sql, new { slot = slotDateTime.ToUniversalTime() });
        return ids.Select(x => (ulong)x).ToHashSet();
    }

    public async Task<IReadOnlyCollection<ulong>> RevokePendingInvitesAsync(int teamSlotId)
    {
        // 額滿後其餘 Invited 已無法接受 → 一次撤銷為 Rejected，RETURNING 回被邀玩家 DiscordId 供通知。
        // 單條 UPDATE 原子完成撤銷＋取名單；呼叫端在 per-team advisory lock 內執行，與定案序列化。
        const string sql = """
            UPDATE "TeamSlotCharacter" SET "Status" = 'Rejected'
            WHERE "TeamSlotId" = @teamSlotId AND "Status" = 'Invited'
            RETURNING "DiscordId";
            """;
        var ids = await _dbContext.QueryAsync<long>(sql, new { teamSlotId });
        return ids.Select(x => (ulong)x).ToList();
    }

    private class MemberRow
    {
        public int Id { get; set; }
        public int TeamSlotId { get; set; }
        public long DiscordId { get; set; }
        public string DiscordName { get; set; } = "";
        public string? CharacterId { get; set; }
        public string Job { get; set; } = "";
        public string Status { get; set; } = "";
        public string Version { get; set; } = "";
    }

    public async Task DeleteByTeamSlotIdAsync(int teamSlotId)
    {
        var sql = new DeleteBuilder<TeamSlotCharacterDbModel>();
        sql.Where(x => x.TeamSlotId == teamSlotId);

        await _dbContext.ExecuteAsync(sql);
    }

    public async Task DeleteCharacterAsync(TeamSlotCharacter teamSlotCharacter)
    {
        var deleteCharacters = new DeleteBuilder<TeamSlotCharacterDbModel>()
            .Where(x => x.Id == teamSlotCharacter.Id);

        await _dbContext.ExecuteAsync(deleteCharacters);

        // 只清除系統自動分配的空團（Source=auto），admin 手動開團不自動刪除
        var deleteEmptySlots = new DeleteBuilder<TeamSlotDbModel>()
            .Where(x => x.Id == teamSlotCharacter.TeamSlotId)
            .Where(x => x.Source == TeamSlotSource.Auto)
            .WhereRaw("""
                      NOT EXISTS (
                      SELECT 1
                      FROM "TeamSlotCharacter" tsc
                      WHERE tsc."TeamSlotId" = a."Id"
                      AND tsc."CharacterId" IS NOT NULL)
                      """);

        await _dbContext.ExecuteAsync(deleteEmptySlots);
    }

    public async Task DeleteByDiscordIdAndPeriodAsync(ulong discordId, DateTimeOffset startDateTime, DateTimeOffset endDateTime)
    {
        // Step 1: 先抓出該期間的 TeamSlot
        var targetSlotsQuery = new QueryBuilder()
            .Select<TeamSlotDbModel>(x => new { x.Id })
            .From<TeamSlotDbModel>()
            .WhereGroup(g =>
            {
                g.Where<TeamSlotDbModel>(x => x.SlotDateTime >= startDateTime)
                    .Where<TeamSlotDbModel>(x => x.SlotDateTime <= endDateTime);
            });

        var targetSlotIds = (await _dbContext.QueryAsync<long>(targetSlotsQuery)).ToList();

        if (!targetSlotIds.Any()) return;

        // Step 2: 將指定 DiscordId 的 TeamSlotCharacter 欄位清空（在該期間）
        var deleteCharacters = new DeleteBuilder<TeamSlotCharacterDbModel>()
            .Where(x => x.DiscordId == (long)discordId)
            .Where(x => targetSlotIds.Contains(x.TeamSlotId));

        await _dbContext.ExecuteAsync(deleteCharacters);

        // Step 3: 只刪除系統自動分配的空團（Source=auto），admin 手動開團不自動刪除
        var deleteEmptySlots = new DeleteBuilder<TeamSlotDbModel>()
            .Where(x => targetSlotIds.Contains(x.Id))
            .Where(x => x.Source == TeamSlotSource.Auto)
            .WhereRaw("""
                      NOT EXISTS (
                      SELECT 1
                      FROM "TeamSlotCharacter" tsc
                      WHERE tsc."TeamSlotId" = a."Id"
                      AND tsc."CharacterId" IS NOT NULL)
                      """);

        await _dbContext.ExecuteAsync(deleteEmptySlots);
    }

    public async Task<bool> UpdateAsync(TeamSlotCharacter teamSlotCharacter)
    {
        var sql = new UpdateBuilder<TeamSlotCharacterDbModel>();
        sql.Set(x => x.DiscordId, (long)teamSlotCharacter.DiscordId)
            .Set(x => x.DiscordName, teamSlotCharacter.DiscordName)
            .Set(x => x.CharacterId, teamSlotCharacter.CharacterId)
            .Set(x => x.CharacterName, teamSlotCharacter.CharacterName)
            .Set(x => x.Job, teamSlotCharacter.Job)
            .Set(x => x.AttackPower, teamSlotCharacter.AttackPower)
            .Set(x => x.Rounds, teamSlotCharacter.Rounds)
            .Set(x => x.IsManual, teamSlotCharacter.IsManual)
            .Where(x => x.Id == teamSlotCharacter.Id)
            // 樂觀鎖：xmin 對不上代表這列這期間被別的流程動過（0 筆受影響）
            .WhereRaw("xmin = @version::xid", new { version = teamSlotCharacter.Version });

        var affected = await _dbContext.ExecuteAsync(sql);
        return affected > 0;
    }
}
