using Application.DTOs;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Query;

public class TeamSlotQuery : ITeamSlotQuery
{
    private readonly IUnitOfWork _unitOfWork;

    public TeamSlotQuery(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }
    
    public async Task<IEnumerable<TeamSlotCharacterDto>> GetByPeriodAndBossIdAsync(Period period, int bossId)
    {
        var sql = new QueryBuilder();
        sql.Select<TeamSlotDbModel>(x => new
            {
                TeamSlotId = x.Id,
                x.SlotDateTime
            })
            .Select<CharacterDbModel>(x=> new
            {
                CharacterId = x.Id,
                CharacterName = x.Name,
                x.Job,
                x.AttackPower
            }, "c")
            .Select<PlayerDbModel>(x=> new
            {
                x.DiscordId,
                x.DiscordName
            }, "d")
            .From<TeamSlotDbModel>()
            .LeftJoin<TeamSlotCharacterDbModel>("""
                                                a."Id" = b."TeamSlotId"
                                                """
            )
            .LeftJoin<CharacterDbModel>("""
                                        b."CharacterId" = c."Id"
                                        """
            )
            .LeftJoin<PlayerDbModel>("""
                                     d."DiscordId" = c."DiscordId"
                                     """
            )
            .Where<TeamSlotDbModel>(x=> period.StartDate <= x.SlotDateTime && period.EndDate >= x.SlotDateTime)
            .Where<TeamSlotDbModel>(x => x.BossId == bossId);

        return await _unitOfWork.QueryAsync<TeamSlotCharacterDto>(sql);
    }
    
    public async Task<IEnumerable<TeamSlotCharacterDto>> GetByPeriodAndDiscordIdAsync(Period period, ulong discordId)
    {
        var sql = new QueryBuilder();
        sql.Select<TeamSlotDbModel>(x => new
            {
                TeamSlotId = x.Id,
                x.SlotDateTime
            })
            .Select<CharacterDbModel>(x=> new
            {
                CharacterId = x.Id,
                CharacterName = x.Name,
                x.Job,
                x.AttackPower
            }, "c")
            .Select<PlayerDbModel>(x=> new
            {
                x.DiscordName
            }, "d")
            .Select<BossDbModel>(x=> new
            {
                BossName = x.Name
            }, "e")
            .From<TeamSlotDbModel>()
            .LeftJoin<TeamSlotCharacterDbModel>("""
                                                a."Id" = b."TeamSlotId"
                                                """
            )
            .LeftJoin<CharacterDbModel>("""
                                        b."CharacterId" = c."Id"
                                        """
            )
            .LeftJoin<PlayerDbModel>("""
                                     c."DiscordId" = d."DiscordId"
                                     """
            )
            .LeftJoin<BossDbModel>("""
                                   a."BossId" = e."Id" 
                                   """
                                   )
            .Where<TeamSlotDbModel>(x=> period.StartDate <= x.SlotDateTime && period.EndDate >= x.SlotDateTime)
            .Where<PlayerDbModel>(x => x.DiscordId == (long)discordId);

        return await _unitOfWork.QueryAsync<TeamSlotCharacterDto>(sql);
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
            .Select<PlayerDbModel>(x => new
            {
                x.DiscordId,
            }, "d")
            .Select<BossDbModel>(x=> new
            {
                BossName = x.Name
            }, "e")
            .From<TeamSlotDbModel>()
            .LeftJoin<TeamSlotCharacterDbModel>("""
                                                a."Id" = b."TeamSlotId"
                                                """
            )
            .LeftJoin<CharacterDbModel>("""
                                        b."CharacterId" = c."Id"
                                        """
            )
            .LeftJoin<PlayerDbModel>("""
                                     c."DiscordId" = d."DiscordId"
                                     """
            )
            .LeftJoin<BossDbModel>("""
                                   e."Id" = a."BossId"
                                   """)
            .Where<TeamSlotDbModel>(x => x.SlotDateTime >= slotDateTime && x.SlotDateTime < end);

        return await _unitOfWork.QueryAsync<TeamSlotCharacterDto>(sql);
    }
}