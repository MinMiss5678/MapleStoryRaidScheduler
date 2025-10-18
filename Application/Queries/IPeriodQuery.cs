using Domain.Entities;

namespace Application.Queries;

public interface IPeriodQuery
{
    Task<int> GetPeriodIdByNowAsync();
    Task<Period> GetByNowAsync();
}