using Application.Interface;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Entities;

namespace Infrastructure.Repositories;

public class BossRepository : IBossRepository
{
    private readonly IUnitOfWork _unitOfWork;

    public BossRepository(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IEnumerable<Boss>> GetAllAsync()
    {
        return await _unitOfWork.Repository<BossDbModel>().GetAllAsync<Boss>(x => new
        {
            x.Id,
            x.Name,
            x.RequireMembers
        });
    }
}