using Application.DTOs;
using Application.Queries;
using Domain.Entities;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Query;

public class TeamSlotQuery : ITeamSlotQuery
{
    private readonly DbContext _dbContext;

    public TeamSlotQuery(DbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    public async Task<IEnumerable<TeamSlotCharacterDto>> GetByPeriodAndBossIdAsync(Period period, int bossId)
    {
        var sql = new QueryBuilder();
        sql.Select<TeamSlotDbModel>(x => new
            {
                TeamSlotId = x.Id,
                x.BossId,
                x.SlotDateTime
            })
            .Select<TeamSlotCharacterDbModel>(x=> new
            {
                TeamSlotCharacterId = x.Id,
                x.CharacterId,
                x.CharacterName,
                x.Job,
                x.AttackPower,
                x.DiscordId,
                x.DiscordName,
                x.Rounds
            }, "b")
            .Select<BossDbModel>(x => new
            {
                BossName = x.Name
            }, "c")
            .From<TeamSlotDbModel>()
            .LeftJoin<TeamSlotCharacterDbModel>("""
                                                a."Id" = b."TeamSlotId"
                                                """
            )
            .LeftJoin<BossDbModel>("""
                                   a."BossId" = c."Id"
                                   """
            )
            .Where<TeamSlotDbModel>(x=> period.StartDate <= x.SlotDateTime && period.EndDate >= x.SlotDateTime)
            .Where<TeamSlotDbModel>(x => x.BossId == bossId);

        return await _dbContext.QueryAsync<TeamSlotCharacterDto>(sql);
    }
    
    public async Task<IEnumerable<TeamSlotCharacterDto>> GetByPeriodAndDiscordIdAsync(Period period, ulong discordId)
    {
        var sql = new QueryBuilder();
        sql.Select<TeamSlotDbModel>(x => new
            {
                TeamSlotId = x.Id,
                x.BossId,
                x.SlotDateTime
            })
            .Select<TeamSlotCharacterDbModel>(x => new
            {
                TeamSlotCharacterId = x.Id,
                x.CharacterId,
                x.CharacterName,
                x.Job,
                x.AttackPower,
                x.DiscordId,
                x.DiscordName,
                x.Rounds
            }, "b")
            .Select<BossDbModel>(x => new
            {
                BossName = x.Name
            }, "c")
            .From<TeamSlotDbModel>()
            .LeftJoin<TeamSlotCharacterDbModel>("""
                                                a."Id" = b."TeamSlotId"
                                                """
            )
            .LeftJoin<BossDbModel>("""
                                   a."BossId" = c."Id" 
                                   """
            )
            .Where<TeamSlotDbModel>(x => period.StartDate <= x.SlotDateTime && period.EndDate >= x.SlotDateTime);

        return await _dbContext.QueryAsync<TeamSlotCharacterDto>(sql);
    }
    
    public async Task<IEnumerable<TeamSlotCharacterDto>> GetBySlotDateTimeAsync(DateTimeOffset slotDateTime)
    {
        var end = slotDateTime.AddDays(1);  
        
        var sql = new QueryBuilder();
        sql.Select<TeamSlotDbModel>(x => new
            {
                TeamSlotId = x.Id,
                x.SlotDateTime
            })
            .Select<TeamSlotCharacterDbModel>(x=> new
            {
                TeamSlotCharacterId = x.Id,
                x.CharacterId,
                x.CharacterName,
                x.Job,
                x.AttackPower,
                x.DiscordId,
                x.DiscordName,
                x.Rounds
            }, "b")
            .Select<BossDbModel>(x=> new
            {
                BossName = x.Name
            }, "e")
            .From<TeamSlotDbModel>()
            .LeftJoin<TeamSlotCharacterDbModel>("""
                                                a."Id" = b."TeamSlotId"
                                                """
            )
            .LeftJoin<BossDbModel>("""
                                   e."Id" = a."BossId"
                                   """)
            .Where<TeamSlotDbModel>(x => x.SlotDateTime >= slotDateTime && x.SlotDateTime < end);

        return await _dbContext.QueryAsync<TeamSlotCharacterDto>(sql);
    }
}