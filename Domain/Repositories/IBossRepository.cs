using Domain.Entities;

namespace Domain.Repositories;

public interface IBossRepository
{
    Task<IEnumerable<Boss>> GetAllAsync();
}