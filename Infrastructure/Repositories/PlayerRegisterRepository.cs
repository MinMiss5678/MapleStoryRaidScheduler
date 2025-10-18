using Application.Interface;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class PlayerRegisterRepository : IPlayerRegisterRepository
{
    private readonly IUnitOfWork _unitOfWork;

    public PlayerRegisterRepository(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IEnumerable<PlayerCharacterRegister>> GetListAsync(ulong discordId, int periodId)
    {
        var builder = new QueryBuilder();

        var sql = builder
            .Select<PlayerRegisterDbModel>(x => new
            {
                x.Id,
                x.PeriodId,
                x.Weekdays,
                x.Timeslots
            })
            .Select<CharacterRegisterDbModel>(x => new
            {
                CharacterRegisterId = x.Id,
                x.CharacterId,
                x.Job,
                x.BossId,
                x.Rounds
            }, "b")
            .Select<Boss>(x => new
            {
                BossName = x.Name
            }, "c")
            .From<PlayerRegisterDbModel>()
            .LeftJoin<CharacterRegisterDbModel>("""
                                                a."Id" = b."PlayerRegisterId"
                                                """)
            .LeftJoin<BossDbModel>("""
                                   b."BossId" = c."Id"
                                   """)
            .Where<PlayerRegisterDbModel>(x => x.DiscordId == (long)discordId && x.PeriodId == periodId);

        return await _unitOfWork.QueryAsync<PlayerCharacterRegister>(sql);
    }

    public async Task<int> CreateAsync(Register register)
    {
        var sql = new InsertBuilder<PlayerRegisterDbModel>();
        sql.Set(x => x.DiscordId, (long)register.DiscordId)
            .Set(x => x.PeriodId, register.PeriodId)
            .Set(x => x.Weekdays, register.Weekdays)
            .Set(x => x.Timeslots, register.Timeslots)
            .ReturnId();

        var id = await _unitOfWork.ExecuteScalarAsync(sql);

        return id;
    }

    public async Task<int> UpdateAsync(Register register)
    {
        var sql = new UpdateBuilder<PlayerRegisterDbModel>();
        sql.Set(x => x.Weekdays, register.Weekdays)
            .Set(x => x.Timeslots, register.Timeslots)
            .Where(x => x.DiscordId == (long)register.DiscordId)
            .Where(x => x.PeriodId == register.PeriodId);

        return await _unitOfWork.ExecuteAsync(sql);
    }

    public async Task<bool> DeleteAsync(ulong discordId, int id)
    {
        var sql = new DeleteBuilder<PlayerRegisterDbModel>();
        sql.Where(x => x.DiscordId == (long)discordId);
        return await _unitOfWork.Repository<PlayerRegisterDbModel>().DeleteAsync(id);
    }
}