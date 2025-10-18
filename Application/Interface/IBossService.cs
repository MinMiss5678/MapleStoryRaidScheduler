using Domain.Entities;

namespace Application.Interface;

public interface IBossService
{
    Task<IEnumerable<Boss>> GetAllAsync();
}