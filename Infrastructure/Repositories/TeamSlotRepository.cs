using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class TeamSlotRepository : ITeamSlotRepository
{
    private readonly DbContext _dbContext;

    public TeamSlotRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> CreateAsync(TeamSlot teamSlot)
    {
        var sql = new InsertBuilder<TeamSlotDbModel>();
        sql.Set(x => x.BossId, teamSlot.BossId)
            .Set(x => x.SlotDateTime, teamSlot.SlotDateTime.ToUniversalTime())
            .Set(x => x.Source, teamSlot.Source)
            .Set(x => x.LeaderDiscordId, teamSlot.LeaderDiscordId.HasValue ? (long?)teamSlot.LeaderDiscordId.Value : null)
            .Set(x => x.Description, teamSlot.Description)
            // period-less（§3.1）：Kind 預設 Scheduled（DB 亦有 DEFAULT），即時團才帶 ExpiresAt；場數範圍選填
            .Set(x => x.Kind, teamSlot.Kind)
            .Set(x => x.ExpiresAt, teamSlot.ExpiresAt?.ToUniversalTime())
            .Set(x => x.RunsMin, teamSlot.RunsMin)
            .Set(x => x.RunsMax, teamSlot.RunsMax)
            .ReturnId();

        return await _dbContext.ExecuteScalarAsync(sql);
    }

    public async Task DeleteAsync(int id)
    {
        var charSql = new DeleteBuilder<TeamSlotCharacterDbModel>();
        charSql.Where(x => x.TeamSlotId == id);
        await _dbContext.ExecuteAsync(charSql);

        await _dbContext.Repository<TeamSlotDbModel>().DeleteAsync(id);
    }

    public async Task<TeamSlot?> GetByIdAsync(int id)
    {
        // 原本是「查 TeamSlot」+「查 Characters」兩次往返，合併成一次 LEFT JOIN：
        // 併發控制迴圈裡每個既有隊伍都會呼叫一次，逐隊各打兩次是可避免的 N+1（見 architecture.md）。
        var sql = new QueryBuilder()
            .Select<TeamSlotDbModel>(x => new { x.Id, x.BossId, x.SlotDateTime, x.Source, x.LeaderDiscordId, x.PendingLeaderDiscordId, x.Kind, x.ExpiresAt, x.RunsMin, x.RunsMax })
            .Select<TeamSlotCharacterDbModel>(x => new
            {
                CharacterRowId = x.Id,
                x.DiscordId,
                x.DiscordName,
                x.CharacterId,
                x.CharacterName,
                x.Job,
                x.AttackPower,
                x.Rounds,
                x.IsManual
            }, "b")
            .From<TeamSlotDbModel>()
            .LeftJoin<TeamSlotCharacterDbModel>("""a."Id" = b."TeamSlotId" """)
            .Where<TeamSlotDbModel>(x => x.Id == id);

        var rows = (await _dbContext.QueryAsync<TeamSlotWithCharacterRow>(sql)).ToList();
        if (rows.Count == 0) return null;

        var first = rows[0];
        // 空隊的 LEFT JOIN 會產生 CharacterRowId=0 的 ghost row（Dapper 對 int? NULL 映射成 int 的慣例），需過濾掉
        var characters = rows
            .Where(r => r.CharacterRowId != 0)
            .Select(r => new TeamSlotCharacter
            {
                Id = r.CharacterRowId,
                TeamSlotId = id,
                DiscordId = (ulong)r.DiscordId,
                DiscordName = r.DiscordName ?? "",
                CharacterId = r.CharacterId,
                CharacterName = r.CharacterName,
                Job = r.Job ?? "",
                AttackPower = r.AttackPower,
                Rounds = r.Rounds,
                IsManual = r.IsManual
            })
            .ToList();

        return new TeamSlot
        {
            Id = first.Id,
            BossId = first.BossId,
            SlotDateTime = first.SlotDateTime,
            Source = first.Source,
            LeaderDiscordId = first.LeaderDiscordId.HasValue ? (ulong?)first.LeaderDiscordId.Value : null,
            PendingLeaderDiscordId = first.PendingLeaderDiscordId.HasValue ? (ulong?)first.PendingLeaderDiscordId.Value : null,
            Kind = first.Kind,
            ExpiresAt = first.ExpiresAt,
            RunsMin = first.RunsMin,
            RunsMax = first.RunsMax,
            Characters = characters
        };
    }

    public async Task SetPendingLeaderAsync(int teamSlotId, ulong? pendingDiscordId)
    {
        // 提議轉讓（設目標）/ 拒絕或作廢（設 null）。低併發，raw UPDATE by Id。
        const string sql = """UPDATE "TeamSlot" SET "PendingLeaderDiscordId" = @pending WHERE "Id" = @id""";
        await _dbContext.ExecuteAsync(sql, new { id = teamSlotId, pending = pendingDiscordId.HasValue ? (long?)pendingDiscordId.Value : null });
    }

    public async Task CompleteLeaderTransferAsync(int teamSlotId, ulong newLeaderDiscordId)
    {
        // 接受轉讓：搬進 LeaderDiscordId、清空 pending。
        const string sql = """UPDATE "TeamSlot" SET "LeaderDiscordId" = @leader, "PendingLeaderDiscordId" = NULL WHERE "Id" = @id""";
        await _dbContext.ExecuteAsync(sql, new { id = teamSlotId, leader = (long)newLeaderDiscordId });
    }

    private class TeamSlotWithCharacterRow
    {
        public int Id { get; set; }
        public int BossId { get; set; }
        public DateTimeOffset SlotDateTime { get; set; }
        public string Source { get; set; } = "";
        public long? LeaderDiscordId { get; set; }
        public long? PendingLeaderDiscordId { get; set; }
        public string Kind { get; set; } = "Scheduled";
        public DateTimeOffset? ExpiresAt { get; set; }
        public int? RunsMin { get; set; }
        public int? RunsMax { get; set; }
        public int CharacterRowId { get; set; }
        public long DiscordId { get; set; }
        public string? DiscordName { get; set; }
        public string? CharacterId { get; set; }
        public string? CharacterName { get; set; }
        public string? Job { get; set; }
        public int AttackPower { get; set; }
        public int Rounds { get; set; }
        public bool IsManual { get; set; }
    }

}
