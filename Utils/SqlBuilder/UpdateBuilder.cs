using System.Linq.Expressions;

namespace Utils.SqlBuilder;

public class UpdateBuilder<T> : SqlCommandBuilder<T>
{
    private Dictionary<string, object> _set = new();

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