using Domain.Entities;

namespace Application.Queries;

public interface IPeriodQuery
{
    Task<int> GetActivePeriodIdAsync();
    Task<int> GetPeriodIdByDateAsync(DateTimeOffset date);
    Task<int> GetLastPeriodIdAsync();
    Task<Period?> GetActivePeriodAsync();
    Task<Period?> GetNextPeriodAsync();
    Task<Period?> GetByIdAsync(int id);
}