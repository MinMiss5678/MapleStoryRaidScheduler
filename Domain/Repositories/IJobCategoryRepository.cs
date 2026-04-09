using Domain.Entities;

namespace Domain.Repositories;

public interface IJobCategoryRepository
{
    Task<IEnumerable<JobCategory>> GetAllAsync();
}
