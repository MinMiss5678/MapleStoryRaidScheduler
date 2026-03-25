using Utils.SqlBuilder;

namespace Application.Interface;

public interface IUnitOfWork
{
    IRepository<T> Repository<T>() where T : class;
    Task BeginTransactionAsync();
    Task CommitAsync();
    Task RollbackAsync();
    Task<int> ExecuteAsync<T>(SqlCommandBuilder<T> builder);
    Task<int> ExecuteScalarAsync<T>(SqlCommandBuilder<T> builder);
    Task<TResult> QuerySingleAsync<TResult>(QueryBuilder builder);
    Task<TResult?> QuerySingleOrDefaultAsync<TResult>(QueryBuilder builder);
    Task<IEnumerable<TResult>> QueryAsync<TResult>(QueryBuilder builder);
}