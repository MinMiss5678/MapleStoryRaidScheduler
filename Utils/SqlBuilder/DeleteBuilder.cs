using System.Linq.Expressions;
using Dapper;

namespace Utils.SqlBuilder;

public class DeleteBuilder<T> : SqlCommandBuilder<T>
{
    private readonly SqlConditionGroup _rootGroup = new("AND", false);

    public DeleteBuilder<T> Where(Expression<Func<T, bool>> expr)
        => AddCondition("AND", expr);

    public DeleteBuilder<T> OrWhere(Expression<Func<T, bool>> expr)
        => AddCondition("OR", expr);

    public DeleteBuilder<T> WhereGroup(Action<DeleteBuilder<T>> action)
    {
        var group = new SqlConditionGroup("AND");
        var builder = new DeleteBuilder<T>(_parameters, group);
        action(builder);
        _rootGroup.Add(group);
        return this;
    }
    
    public DeleteBuilder<T> WhereRaw(string sql)
    {
        _rootGroup.Add(new SqlCondition("AND", sql));
        return this;
    }

    private DeleteBuilder(DynamicParameters parameters, SqlConditionGroup group)
    {
        _parameters = parameters;
        _rootGroup = group;
    }

    public DeleteBuilder() { }

    private DeleteBuilder<T> AddCondition(string op, Expression<Func<T, bool>> expr)
    {
        var visitor = new SqlExpressionVisitor("a", _parameters); // 🔥 固定 alias
        var condition = visitor.Translate(expr.Body);
        _rootGroup.Add(new SqlCondition(op, condition));
        return this;
    }

    protected override string BuildCommand()
    {
        if (!_rootGroup.Conditions.Any())
            throw new InvalidOperationException("Delete must have a WHERE clause!");

        var table = GetTableName();

        var sql = $"DELETE FROM \"{table}\" AS a";

        sql += $" WHERE {_rootGroup.ToSql()}";

        return sql;
    }

}