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
        // 隊伍不再是充血聚合、成員清單由 query 端各自負責（確認人數走 TeamMembershipQuery），
        // 故此處只讀 TeamSlot 單表；不再 JOIN TeamSlotCharacter。
        var sql = new QueryBuilder()
            .Select<TeamSlotDbModel>(x => new { x.Id, x.BossId, x.SlotDateTime, x.Source, x.LeaderDiscordId, x.PendingLeaderDiscordId, x.Kind, x.ExpiresAt, x.RunsMin, x.RunsMax })
            .From<TeamSlotDbModel>()
            .Where<TeamSlotDbModel>(x => x.Id == id);

        var row = await _dbContext.QuerySingleOrDefaultAsync<TeamSlotDbModel>(sql);
        if (row == null) return null;

        return new TeamSlot
        {
            Id = row.Id,
            BossId = row.BossId,
            SlotDateTime = row.SlotDateTime,
            Source = row.Source,
            LeaderDiscordId = row.LeaderDiscordId.HasValue ? (ulong?)row.LeaderDiscordId.Value : null,
            PendingLeaderDiscordId = row.PendingLeaderDiscordId.HasValue ? (ulong?)row.PendingLeaderDiscordId.Value : null,
            Kind = row.Kind,
            ExpiresAt = row.ExpiresAt,
            RunsMin = row.RunsMin,
            RunsMax = row.RunsMax
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


}
