using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;

namespace Utils.SqlBuilder;

public class DeleteBuilder<T> : SqlCommandBuilder<T>
{
    public DeleteBuilder<T> Where(Expression<Func<T, bool>> expr)
    {
        var visitor = new SqlExpressionVisitor(null, _parameters);
        _wheres.Add(visitor.Translate(expr));
        return this;
    }
    
    protected override string BuildCommand()
    {
        if (_wheres.Count == 0) throw new InvalidOperationException("Delete must have a WHERE clause!");
        var table = GetTableName();
        return $"DELETE FROM \"{table}\" WHERE " + string.Join(" AND ", _wheres);
    }
    
    protected string GetTableName()
    {
        var type = typeof(T);
        var attr = type.GetCustomAttribute<TableAttribute>();
        var tableName = attr != null ? attr.Name : type.Name;
        return tableName;
    }
}