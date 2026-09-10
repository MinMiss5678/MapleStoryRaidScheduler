using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Application.Interface;
using Dapper;
using Microsoft.Extensions.Logging;
using Utils.SqlBuilder;

namespace Infrastructure.Dapper;

public class DbContext
{
    public IDbConnection Connection { get; }
    public IDbTransaction? Transaction { get; private set; }
    private bool _completed;

    // 壓測用:量「取得DB連線耗時」(Npgsql pool rent — pool 滿時 OpenAsync 會等到有連線釋出)。optional logger → 不動 DI/測試建構子;
    // 部署時 DI 注入真 logger,connection_acquire_ms 進 Serilog → 坐實「client 延遲多出的秒數在等連線、非等鎖」。
    private readonly ILogger<DbContext>? _logger;

    public DbContext(IDbConnection connection, ILogger<DbContext>? logger = null)
    {
        Connection = connection;
        _logger = logger;
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
            {
                // OpenAsync 從 Npgsql pool 租一條連線；pool 滿（VUS > Maximum Pool Size）時會在此等到有連線釋出 → 這段＝真正的「取得DB連線」耗時。
                var sw = Stopwatch.StartNew();
                await dbConnection.OpenAsync();
                sw.Stop();
                _logger?.LogInformation("connection_acquire_ms={Ms}", sw.ElapsedMilliseconds);
            }
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
