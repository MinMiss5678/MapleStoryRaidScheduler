using System.Data;
using Application.Interface;
using Dapper;
using Utils.SqlBuilder;

namespace Infrastructure.Dapper;

public class DbContext
{
    public IDbConnection Connection { get; }
    public IDbTransaction Transaction { get; private set; }

    // 供測試動態代理/Mock 使用的參數less 建構子（Castle 動態代理需要）
    protected DbContext()
    {
        Connection = default!; // 僅供測試 Mock 使用，不會實際呼叫非虛擬成員
    }

    public DbContext(IDbConnection connection)
    {
        Connection = connection;
    }

    public void Begin()
    {
        if (Transaction == null)
            Transaction = Connection.BeginTransaction();
    }

    public void Commit()
    {
        Transaction?.Commit();
        Transaction = null;
    }

    public void Rollback()
    {
        Transaction?.Rollback();
        Transaction = null;
    }

    public virtual async Task<int> ExecuteAsync<T>(SqlCommandBuilder<T> builder)
    {
        var (sql, param) = builder.Build();
        return await Connection.ExecuteAsync(sql, param, Transaction);
    }  
    
    public virtual async Task<int> ExecuteAsync(string sql, object param)
    {
        return await Connection.ExecuteAsync(sql, param, Transaction);
    }

    public virtual async Task<int> ExecuteScalarAsync<T>(SqlCommandBuilder<T> builder)
    {
        var (sql, param) = builder.Build();
        return await Connection.ExecuteScalarAsync<int>(sql, param, Transaction);
    }

    public virtual async Task<IEnumerable<TResult>> QueryAsync<TResult>(string sql, object param)
    {
        return await Connection.QueryAsync<TResult>(sql, param, Transaction);
    }

    public virtual async Task<TResult> QuerySingleAsync<TResult>(QueryBuilder builder)
    {
        var (sql, param) = builder.Build();
        return await Connection.QuerySingleAsync<TResult>(sql, param, Transaction);
    }
    
    public virtual async Task<TResult?> QuerySingleOrDefaultAsync<TResult>(QueryBuilder builder)
    {
        var (sql, param) = builder.Build();
        return await Connection.QuerySingleOrDefaultAsync<TResult?>(sql, param, Transaction) ;
    }

    public virtual async Task<IEnumerable<TResult>> QueryAsync<TResult>(QueryBuilder builder)
    {
        var (sql, param) = builder.Build();
        return await Connection.QueryAsync<TResult>(sql, param, Transaction);
    }
    
    public virtual IRepository<T> Repository<T>() where T : class
    {
        return new DapperRepository<T>(Connection, Transaction);
    }
}