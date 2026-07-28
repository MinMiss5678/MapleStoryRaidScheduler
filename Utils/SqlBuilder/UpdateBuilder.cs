using System.Linq.Expressions;
using Dapper;

namespace Utils.SqlBuilder;

public class UpdateBuilder<T> : SqlCommandBuilder<T>
{
    private Dictionary<string, object?> _set = new();

    public UpdateBuilder<T> Set<TProp>(Expression<Func<T, TProp>> column, TProp value)
    {
        _set.Add(GetMemberName(column), value);
        return this;
    }

    public UpdateBuilder<T> Where(Expression<Func<T, bool>> expr)
    {
        var visitor = new SqlExpressionVisitor(null, _parameters);
        var condition = visitor.Translate(expr);
        _wheres.Add(condition);
        return this;
    }

    /// <summary>加一段無法用一般欄位表達式表達的原生 WHERE 條件（例如樂觀鎖的 xmin 版本比對）。</summary>
    public UpdateBuilder<T> WhereRaw(string sql, object? parameters = null)
    {
        _wheres.Add(sql);
        if (parameters != null)
            _parameters.AddDynamicParams(parameters);
        return this;
    }

    protected override string BuildCommand()
    {
        if (_set.Count == 0)
            return "SELECT 0"; // No-op SQL

        if (_wheres.Count == 0)
            throw new InvalidOperationException("UPDATE without WHERE is not allowed.");

        var setParts = new List<string>();
        foreach (var kv in _set)
        {
            var param = $"set_{kv.Key}";
            setParts.Add($"\"{kv.Key}\" = @{param}");
            _parameters.Add(param, kv.Value);
        }

        var sql = $"UPDATE \"{GetTableName()}\" SET {string.Join(", ", setParts)}";
        sql += " WHERE " + string.Join(" AND ", _wheres);
        return sql;
    }

    private static string GetMemberName<TProp>(Expression<Func<T, TProp>> expr)
    {
        if (expr.Body is MemberExpression m) return m.Member.Name;
        throw new InvalidOperationException("Expression must be a member access, e.g. x => x.Property");
    }
}
