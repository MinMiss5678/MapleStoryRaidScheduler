using Domain.Entities;

namespace Domain.Repositories;

public interface IPeriodRepository
{
    Task CreateAsync(Period period);
    Task<bool> ExistByStartDateAsync(DateTimeOffset startDate);
}