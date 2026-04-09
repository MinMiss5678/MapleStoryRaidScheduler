using Domain.Entities;

namespace Application.Queries;

public interface IPeriodQuery
{
    Task<int> GetPeriodIdByNowAsync();
    Task<int> GetPeriodIdByDateAsync(DateTimeOffset date);
    Task<int> GetLastPeriodIdAsync();
    Task<Period?> GetByNowAsync();
    Task<Period?> GetByIdAsync(int id);
}