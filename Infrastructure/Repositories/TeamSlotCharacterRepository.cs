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
            IsManual = teamSlot.IsManual
        });
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

        var deleteEmptySlots = new DeleteBuilder<TeamSlotDbModel>()
            .Where(x => x.Id == teamSlotCharacter.TeamSlotId)
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
                    .Where<TeamSlotDbModel>(x => x.SlotDateTime <=  endDateTime);
            });

        var targetSlotIds = (await _dbContext.QueryAsync<long>(targetSlotsQuery)).ToList();

        if (!targetSlotIds.Any()) return;

        // Step 2: 將指定 DiscordId 的 TeamSlotCharacter 欄位清空（在該期間）
        var deleteCharacters = new DeleteBuilder<TeamSlotCharacterDbModel>()
            .Where(x => x.DiscordId == (long)discordId)
            .Where(x => targetSlotIds.Contains(x.TeamSlotId));

        await _dbContext.ExecuteAsync(deleteCharacters);

        // Step 3: 刪除空的 TeamSlot
        var deleteEmptySlots = new DeleteBuilder<TeamSlotDbModel>()
            .Where(x => targetSlotIds.Contains(x.Id))
            .WhereRaw("""
                      NOT EXISTS (
                      SELECT 1
                      FROM "TeamSlotCharacter" tsc
                      WHERE tsc."TeamSlotId" = a."Id"
                      AND tsc."CharacterId" IS NOT NULL)
                      """);

        await _dbContext.ExecuteAsync(deleteEmptySlots);
    }

    public async Task UpdateAsync(TeamSlotCharacter teamSlotCharacter)
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
            .Where(x => x.Id == teamSlotCharacter.Id);

        await _dbContext.ExecuteAsync(sql);
    }
}