using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Dapper;

namespace Utils.SqlBuilder;

public abstract class SqlCommandBuilder<T>
{
    protected List<string> _wheres = new();
    protected DynamicParameters _parameters = new();

    protected abstract string BuildCommand();

    public (string Sql, DynamicParameters Params) Build()
    {
        return (BuildCommand(), _parameters);
    }

    // 從 [Table] attribute 取得資料表名稱，fallback 為 class 名稱
    protected static string GetTableName()
    {
        var type = typeof(T);
        var attr = type.GetCustomAttribute<TableAttribute>();
        return attr != null ? attr.Name : type.Name;
    }
}