using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;

namespace Utils.SqlBuilder;

public class UpdateBuilder<T> : SqlCommandBuilder<T>
{
    private Dictionary<string, object> _set = new();
    protected virtual string Quote(string name) => $"\"{name}\"";

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
        {
            return "SELECT 0"; // No-op SQL
        }
        
        if (_wheres.Count == 0)
            throw new InvalidOperationException("UPDATE without WHERE is not allowed.");
        
        var setParts = new List<string>();

        foreach (var kv in _set)
        {
            var param = $"set_{kv.Key}";
            setParts.Add($"{Quote(kv.Key)} = @{param}");
            _parameters.Add(param, kv.Value);
        }

        var setPart = string.Join(", ", _set.Keys.Select(k => $"\"{k}\" = @{k}"));
        
        var table = Quote(GetTableName());
        var sql = $"UPDATE {table} SET {string.Join(", ", setParts)}";

        if (_wheres.Count > 0)
            sql += " WHERE " + string.Join(" AND ", _wheres);
        return sql;
    }

    private string GetMemberName<TProp>(Expression<Func<T, TProp>> expr)
    {
        if (expr.Body is MemberExpression m) return m.Member.Name;
        throw new InvalidOperationException("Expression must be a member access, e.g. x => x.Property");
    }
    
    protected string GetTableName()
    {
        var type = typeof(T);
        var attr = type.GetCustomAttribute<TableAttribute>();
        var tableName = attr != null ? attr.Name : type.Name;
        return tableName;
    }
}