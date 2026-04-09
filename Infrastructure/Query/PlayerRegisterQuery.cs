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
        sql.Select<CharacterRegisterDbModel>(x => new
            {
                x.Id,
                x.CharacterId,
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
                x.Job,
                x.AttackPower
            }, "d")
            .Select<PlayerAvailabilityDbModel>(x => new
            {
                x.Weekday,
                x.StartTime,
                x.EndTime
            }, "e")
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
            .LeftJoin<PlayerAvailabilityDbModel>("""
                                                 a."Id" = e."PlayerRegisterId"
                                                 """)
            .Where<PlayerRegisterDbModel>(x => x.PeriodId == period)
            .Where<CharacterRegisterDbModel>(x => x.BossId == bossId);

        var data = await _dbContext.QueryAsync<dynamic>(sql);
        
        // 由於 Join 會產生重複的角色行（不同的 Availability），需要進行 GroupBy
        var result = data.GroupBy(x => (int)x.Id)
            .Select(g => {
                var first = g.First();
                return new PlayerRegisterSchedule
                {
                    Id = (int)first.Id,
                    DiscordId = (ulong)first.DiscordId,
                    DiscordName = (string)first.DiscordName,
                    CharacterId = (string)first.CharacterId,
                    CharacterName = (string)first.CharacterName,
                    Job = (string)first.Job,
                    AttackPower = (int)first.AttackPower,
                    Rounds = (int)first.Rounds,
                    Availabilities = g.Select(x => new PlayerAvailability
                    {
                        Weekday = (int)x.Weekday,
                        StartTime = (TimeOnly)x.StartTime,
                        EndTime = (TimeOnly)x.EndTime
                    }).ToList()
                };
            });

        return result;
    }
    
    public async Task<IEnumerable<PlayerRegisterSchedule>> GetByQueryAsync(RegisterGetByQueryRequest request, int periodId)
    {
        var sql = new QueryBuilder();
        sql.Select<CharacterRegisterDbModel>(x => new
            {
                x.CharacterId,
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
                x.Job,
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