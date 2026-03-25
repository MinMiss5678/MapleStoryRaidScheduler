using System.Data;
using Application.Interface;
using Dapper;
using Npgsql;
using Utils.SqlBuilder;

namespace Infrastructure.Dapper;

public class UnitOfWork : IUnitOfWork
{
    private readonly NpgsqlConnection _connection;
    private NpgsqlTransaction? _transaction;
    private readonly Dictionary<Type, object> _repositories = new();

    public UnitOfWork(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    public IRepository<T> Repository<T>() where T : class
    {
        var type = typeof(T);
        if (!_repositories.ContainsKey(type))
        {
            _repositories[type] = new DapperRepository<T>(_connection, _transaction);
        }

        return (IRepository<T>)_repositories[type];
    }

    public async Task BeginTransactionAsync()
    {
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync();
            _transaction = await _connection.BeginTransactionAsync();
        }
    }

    public async Task CommitAsync()
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync();
            _transaction = null;
        }
    }

    public async Task RollbackAsync()
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync();
            _transaction = null;
        }
    }

    public async Task<int> ExecuteAsync<T>(SqlCommandBuilder<T> builder)
    {
        var (sql, param) = builder.Build();
        return await _connection.ExecuteAsync(sql, param, _transaction);
    }

    public async Task<int> ExecuteScalarAsync<T>(SqlCommandBuilder<T> builder)
    {
        var (sql, param) = builder.Build();
        return await _connection.ExecuteScalarAsync<int>(sql, param, _transaction);
    }

    public async Task<TResult> QuerySingleAsync<TResult>(QueryBuilder builder)
    {
        var (sql, param) = builder.Build();
        return await _connection.QuerySingleAsync<TResult>(sql, param, _transaction);
    }
    
    public async Task<TResult?> QuerySingleOrDefaultAsync<TResult>(QueryBuilder builder)
    {
        var (sql, param) = builder.Build();
        return await _connection.QuerySingleOrDefaultAsync<TResult?>(sql, param, _transaction) ;
    }

    public async Task<IEnumerable<TResult>> QueryAsync<TResult>(QueryBuilder builder)
    {
        var (sql, param) = builder.Build();
        return await _connection.QueryAsync<TResult>(sql, param, _transaction);
    }
}