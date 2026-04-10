using System.Linq.Expressions;

namespace Utils.SqlBuilder;

public class InsertBuilder<T> : SqlCommandBuilder<T>
{
    private Dictionary<string, object> _columns = new();
    private bool _returnId = false;

    public InsertBuilder<T> Set<TProp>(Expression<Func<T, TProp>> column, TProp value)
    {
        _columns.Add(GetMemberName(column), value);
        return this;
    }

    public InsertBuilder<T> ReturnId()
    {
        _returnId = true;
        return this;
    }

    protected override string BuildCommand()
    {
        var cols = string.Join(", ", _columns.Keys.Select(k => $"\"{k}\""));
        var vals = string.Join(", ", _columns.Keys.Select(k => $"@{k}"));
        foreach (var kv in _columns) _parameters.Add(kv.Key, kv.Value);
        var sql = $"INSERT INTO \"{GetTableName()}\" ({cols}) VALUES ({vals})";
        if (_returnId)
            sql += " RETURNING \"Id\"";

        return sql;
    }

    private static string GetMemberName<TProp>(Expression<Func<T, TProp>> expr)
    {
        if (expr.Body is MemberExpression m) return m.Member.Name;
        throw new InvalidOperationException();
    }
}