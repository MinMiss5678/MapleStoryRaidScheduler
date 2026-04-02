using System.Data;
using Utils.SqlBuilder;

namespace Application.Interface;

public interface IUnitOfWork
{
    Task BeginAsync();
    Task CommitAsync();
    Task RollbackAsync();
}