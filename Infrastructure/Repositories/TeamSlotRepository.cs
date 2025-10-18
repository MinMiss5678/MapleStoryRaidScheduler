using Application.Interface;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class TeamSlotRepository : ITeamSlotRepository
{
    private readonly IUnitOfWork _unitOfWork;

    public TeamSlotRepository(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<int> CreateAsync(TeamSlot teamSlot)
    {
        var sql = new InsertBuilder<TeamSlotDbModel>();
        sql.Set(x => x.BossId, teamSlot.BossId)
            .Set(x => x.SlotDateTime, teamSlot.SlotDateTime)
            .ReturnId();

        return await _unitOfWork.ExecuteScalarAsync(sql);
    }

    public async Task DeleteAsync(int id)
    {
        await _unitOfWork.Repository<TeamSlotDbModel>().DeleteAsync(id);
    }
}