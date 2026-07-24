using System.Data;
using System.Data.Common;
using Application.Interface;
using Dapper;
using Utils.SqlBuilder;

namespace Infrastructure.Dapper;

public class DbContext
{
    public IDbConnection Connection { get; }
    public IDbTransaction? Transaction { get; private set; }
    private bool _completed;

    public DbContext(IDbConnection connection)
    {
        Connection = connection;
    }

    public async Task BeginAsync()
    {
        _completed = false;
        // 真非同步：真 DbConnection 走 async I/O（等 DB 往返時把執行緒還給池子）；
        // 測試用的 mock IDbConnection 沒有 async API → 退回同步。
        // 連線延遲開啟（工廠不再 eager Open）→ 開交易前先確保連線已開。
        if (Connection is DbConnection dbConnection)
        {
            if (dbConnection.State != ConnectionState.Open)
                await dbConnection.OpenAsync();
            Transaction ??= await dbConnection.BeginTransactionAsync();
        }
        else
        {
            if (Connection.State != ConnectionState.Open)
                Connection.Open();
            Transaction ??= Connection.BeginTransaction();
        }
    }

    public async Task CommitAsync()
    {
        if (Transaction is DbTransaction dbTransaction)
            await dbTransaction.CommitAsync();
        else
            Transaction?.Commit();
        Transaction = null;
        _completed = true;
    }

    public async Task RollbackAsync()
    {
        if (Transaction is DbTransaction dbTransaction)
            await dbTransaction.RollbackAsync();
        else
            Transaction?.Rollback();
        Transaction = null;
        _completed = true;
    }

    private void EnsureNotCompleted()
    {
        if (_completed)
            throw new InvalidOperationException("Transaction has already been committed or rolled back. No further operations are allowed.");
    }

    public virtual async Task<int> ExecuteAsync<T>(SqlCommandBuilder<T> builder)
    {
        EnsureNotCompleted();
        var (sql, param) = builder.Build();
        return await Connection.ExecuteAsync(sql, param, Transaction);
    }

    public virtual async Task<int> ExecuteAsync(string sql, object param)
    {
        EnsureNotCompleted();
        return await Connection.ExecuteAsync(sql, param, Transaction);
    }

    public virtual async Task<int> ExecuteScalarAsync<T>(SqlCommandBuilder<T> builder)
    {
        EnsureNotCompleted();
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
        return await Connection.QuerySingleOrDefaultAsync<TResult?>(sql, param, Transaction);
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
