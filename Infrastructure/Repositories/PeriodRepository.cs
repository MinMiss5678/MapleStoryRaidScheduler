using Application.Interface;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Entities;

namespace Infrastructure.Repositories;

public class PeriodRepository : IPeriodRepository
{
    private readonly IUnitOfWork _unitOfWork;

    public PeriodRepository(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task CreateAsync(Period period)
    {
        await _unitOfWork.Repository<PeriodDbModel>().InsertAsync(new PeriodDbModel()
        {
            StartDate = period.StartDate,
            EndDate = period.EndDate
        });
    }

    public async Task<bool> ExistByStartDateAsync(DateTimeOffset startDate)
    {
        return await _unitOfWork.Repository<PeriodDbModel>().ExistAsync(x=>x.StartDate == startDate);
    }
}