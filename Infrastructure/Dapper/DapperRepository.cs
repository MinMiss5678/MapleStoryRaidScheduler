using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using Application.Interface;
using Dapper;
using Domain.Attributes;
using Utils.SqlBuilder;

namespace Infrastructure.Dapper;

public class DapperRepository<T> : IRepository<T> where T : class
{
    private readonly IDbConnection _connection;
    private readonly IDbTransaction? _transaction;

    private readonly string _tableName;
    private readonly string _keyName;
    private readonly bool _isExplicitKey;

    public DapperRepository(IDbConnection connection, IDbTransaction? transaction = null)
    {
        _connection = connection;
        _transaction = transaction;

        var tableAttr = typeof(T).GetCustomAttribute<TableAttribute>();
        _tableName = tableAttr != null ? tableAttr.Name : typeof(T).Name;

        var keyProp = typeof(T).GetProperties()
                          .FirstOrDefault(p => p.GetCustomAttribute<ExplicitKeyAttribute>() != null)
                      ?? typeof(T).GetProperties()
                          .FirstOrDefault(p => p.GetCustomAttribute<KeyAttribute>() != null)
                      ?? typeof(T).GetProperty("Id")
                      ?? throw new InvalidOperationException($"Entity {typeof(T).Name} 沒有 [Key]、[ExplicitKey] 或 Id 屬性");

        _isExplicitKey = keyProp.GetCustomAttribute<ExplicitKeyAttribute>() != null;

        _keyName = keyProp.Name;
    }

    public async Task<IEnumerable<TResult>> GetAllAsync<TResult>(Expression<Func<T, object>>? columns = null)
    {
        string columnList;
        if (columns != null)
        {
            if (columns.Body is NewExpression newExp)
            {
                var names = newExp.Members!.Select(m => $"\"{m.Name}\"");
                columnList = string.Join(",", names);
            }
            else if (columns.Body is MemberExpression m)
            {
                columnList = $"\"{m.Member.Name}\"";
            }
            else if (columns.Body is UnaryExpression u && u.Operand is MemberExpression um)
            {
                columnList = $"\"{um.Member.Name}\"";
            }
            else
            {
                throw new Exception("Unsupported expression");
            }
        }
        else
        {
            columnList = "*";
        }

        var sql = $"SELECT {columnList} FROM \"{_tableName}\"";
        return await _connection.QueryAsync<TResult>(sql, transaction: _transaction);
    }
    
    public async Task<T?> GetByIdAsync(object id, params Expression<Func<T, object>>[] columns)
    {
        string columnList;

        if (columns != null && columns.Length > 0)
        {
            var names = columns.Select(exp =>
            {
                if (exp.Body is MemberExpression member)
                    return $"\"{member.Member.Name}\"";
                if (exp.Body is UnaryExpression unary && unary.Operand is MemberExpression unaryMember)
                    return $"\"{unaryMember.Member.Name}\"";
                throw new Exception("Unsupported expression");
            });
            columnList = string.Join(",", names);
        }
        else
        {
            columnList = "*"; // 如果沒指定，全部欄位
        }

        var sql = $"SELECT {columnList} FROM \"{_tableName}\" WHERE \"{_keyName}\"=@Id";

        return await _connection.QueryFirstOrDefaultAsync<T>(sql, new { Id = id }, _transaction);
    }
    
    public async Task<bool> ExistAsync(object id)
    {
        var sql = $"SELECT 1 FROM \"{_tableName}\" WHERE \"{_keyName}\" = @Id LIMIT 1";
        var result = await _connection.ExecuteScalarAsync<int?>(sql, new { Id = id }, _transaction);
        return result.HasValue;
    }
    
    public async Task<bool> ExistAsync(Expression<Func<T, bool>> predicate)
    {
        var builder = new QueryBuilder();
        builder.From<T>().Where(predicate);

        var (sql, parameters) = builder.Build();
        sql = sql.Replace("SELECT *", "SELECT 1");

        var result = await _connection.ExecuteScalarAsync<int?>(sql, parameters, _transaction);
        return result.HasValue;
    }

    public async Task<int> InsertAsync(T entity)
    {
        var sql = GenerateInsertSql(entity);
        return await _connection.ExecuteAsync(sql, entity, _transaction);
    }

    public async Task<int> UpdateAsync(T entity)
    {
        var sql = GenerateUpdateSql(entity);
        return await _connection.ExecuteAsync(sql, entity, _transaction);
    }

    public async Task<bool> DeleteAsync(object id)
    {
        var sql = $"DELETE FROM \"{_tableName}\" WHERE \"{_keyName}\"=@Id";
        var affectedRows = await _connection.ExecuteAsync(sql, new { Id = id }, _transaction);
        return affectedRows > 0;
    }

    // ====== 自動產生 SQL ======
    private string GenerateInsertSql(T entity)
    {
        var props = typeof(T).GetProperties()
            .Where(p => _isExplicitKey || p.Name != _keyName);
        var columns = string.Join(",", props.Select(p => $"\"{p.Name}\""));
        var values = string.Join(",", props.Select(p => $"@{p.Name}"));
        return $"INSERT INTO \"{_tableName}\" ({columns}) VALUES ({values})";
    }

    private string GenerateUpdateSql(T entity)
    {
        var props = typeof(T).GetProperties().Where(p => p.Name != _keyName);
        var setClause = string.Join(",", props.Select(p => $"\"{p.Name}\"=@{p.Name}"));
        return $"UPDATE \"{_tableName}\" SET {setClause} WHERE \"{_keyName}\"=@{_keyName}";
    }

}