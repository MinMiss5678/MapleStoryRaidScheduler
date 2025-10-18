using System.Linq.Expressions;
using Dapper;

namespace Utils.SqlBuilder;

public abstract class SqlCommandBuilder<T>
{
    protected List<string> _wheres = new();
    protected DynamicParameters _parameters = new();

    public SqlCommandBuilder<T> Where(Expression<Func<T, bool>> expr)
    {
        var sql = ExpressionToSql(expr); // 解析 Expression 成 SQL
        _wheres.Add(sql);
        return this;
    }

    protected abstract string BuildCommand();

    public (string Sql, DynamicParameters Params) Build()
    {
        return (BuildCommand(), _parameters);
    }

    private string ExpressionToSql(Expression expr)
    {
        // TODO: 實作 Expression -> SQL
        // 例如 x => x.Id == 1 會轉成 "Id = @Id"
        throw new NotImplementedException();
    }
}