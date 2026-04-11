using System.Linq.Expressions;

namespace Application.Interface;

public interface IRepository<T> where T : class
{
    Task<IEnumerable<TResult>> GetAllAsync<TResult>(Expression<Func<T, object>>? columns = null);
    Task<T?> GetByIdAsync(object id, params Expression<Func<T, object>>[] columns);
    Task<bool> ExistAsync(object id);
    Task<bool> ExistAsync(Expression<Func<T, bool>> predicate);
    Task<int> InsertAsync(T entity);
    Task<int> UpdateAsync(T entity);
    Task<bool> DeleteAsync(object id);
}