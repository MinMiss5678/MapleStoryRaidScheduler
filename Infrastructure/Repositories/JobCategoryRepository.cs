using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;

namespace Infrastructure.Repositories;

public class JobCategoryRepository : IJobCategoryRepository
{
    private readonly DbContext _dbContext;

    public JobCategoryRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<JobCategory>> GetAllAsync()
    {
        return await _dbContext.Repository<JobCategoryDbModel>().GetAllAsync<JobCategory>(x => new
        {
            x.CategoryName,
            x.JobName
        });
    }
}
