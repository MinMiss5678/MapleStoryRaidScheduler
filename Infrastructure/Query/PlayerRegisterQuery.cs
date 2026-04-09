using Application.DTOs;
using Application.Queries;
using Domain.Entities;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Query;

public class PlayerRegisterQuery : IPlayerRegisterQuery
{
    private readonly IPeriodQuery _periodQuery;
    private readonly DbContext _dbContext;

    public PlayerRegisterQuery(IPeriodQuery periodQuery, DbContext dbContext)
    {
        _periodQuery = periodQuery;
        _dbContext = dbContext;
    }
    
    public async Task<IEnumerable<PlayerRegisterSchedule>> GetByNowPeriodIdAsync(int bossId)
    {
        var period = await _periodQuery.GetPeriodIdByNowAsync();
        var sql = new QueryBuilder();
        sql.Select<PlayerRegisterDbModel>(x=> new
            {
                x.Weekdays,
                x.Timeslots,
            })
            .Select<CharacterRegisterDbModel>(x => new
            {
                x.Id,
                x.CharacterId,
                x.Job,
                x.Rounds
            }, "b")
            .Select<PlayerDbModel>(x => new
            {
                x.DiscordId,
                x.DiscordName
            }, "c")
            .Select<CharacterDbModel>(x => new
            {
                CharacterName = x.Name,
                x.AttackPower
            }, "d")
            .From<PlayerRegisterDbModel>()
            .LeftJoin<CharacterRegisterDbModel>("""
                                                a."Id" = b."PlayerRegisterId"
                                                """)
            .LeftJoin<PlayerDbModel>("""
                                     a."DiscordId" = c."DiscordId"
                                     """)
            .LeftJoin<CharacterDbModel>("""
                                        b."CharacterId" = d."Id"
                                        """)
            .Where<PlayerRegisterDbModel>(x => x.PeriodId == period)
            .Where<CharacterRegisterDbModel>(x => x.BossId == bossId);

        return await _unitOfWork.QueryAsync<PlayerRegisterSchedule>(sql);
    }
    
    public async Task<IEnumerable<PlayerRegisterSchedule>> GetByQueryAsync(RegisterGetByQueryRequest request, int periodId)
    {
        var sql = new QueryBuilder();
        sql.Select<CharacterRegisterDbModel>(x => new
            {
                x.CharacterId,
                x.Job,
                x.Rounds
            }, "b")
            .Select<PlayerDbModel>(x => new
            {
                x.DiscordId,
                x.DiscordName
            }, "c")
            .Select<CharacterDbModel>(x => new
            {
                CharacterName = x.Name,
                x.AttackPower
            }, "d")
            .From<PlayerRegisterDbModel>()
            .LeftJoin<CharacterRegisterDbModel>("""
                                                a."Id" = b."PlayerRegisterId"
                                                """)
            .LeftJoin<PlayerDbModel>("""
                                     a."DiscordId" = c."DiscordId"
                                     """)
            .LeftJoin<CharacterDbModel>("""
                                        b."CharacterId" = d."Id"
                                        """)
            .Where<PlayerRegisterDbModel>(x => x.PeriodId == periodId)
            .Where<CharacterRegisterDbModel>(x => x.BossId == request.BossId);
            
        bool hasQuery = !string.IsNullOrEmpty(request.Query);
        bool hasJob = !string.IsNullOrEmpty(request.Job);
        if (!hasQuery && hasJob)
        {
            sql.WhereGroup(g => { g.OrWhere<CharacterDbModel>(x => x.Job == request.Job); });
        }
        else if (hasQuery && !hasJob)
        {
            sql.WhereGroup(g =>
            {
                g.OrWhere<PlayerDbModel>(x => x.DiscordName == request.Query)
                    .OrWhere<CharacterDbModel>(x => x.Name == request.Query);
            });
        }
        else if (hasQuery && hasJob)
        {
            sql.Where<CharacterDbModel>(x => x.Job == request.Job)
                .WhereGroup(sub =>
                {
                    sub.OrWhere<PlayerDbModel>(x => x.DiscordName == request.Query)
                        .OrWhere<CharacterDbModel>(x => x.Name == request.Query);
                });
        }

        return await _dbContext.QueryAsync<PlayerRegisterSchedule>(sql);
    }
}