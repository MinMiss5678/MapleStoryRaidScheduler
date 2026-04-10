using System.Linq.Expressions;
using Dapper;

namespace Utils.SqlBuilder;

/// <summary>
/// 單資料表的型別安全查詢建構器，方法不需重複傳入泛型型別。
/// 可透過隱含轉換傳給接受 QueryBuilder 的 DbContext 方法。
/// 有 JOIN 需求時請直接使用 QueryBuilder。
/// </summary>
public class TypedQueryBuilder<T>
{
    private readonly QueryBuilder _inner;

    public TypedQueryBuilder()
    {
        _inner = new QueryBuilder();
        _inner.From<T>();
    }

    // 供 WhereGroup / OrWhereGroup 內部使用，不重複呼叫 From<T>
    internal TypedQueryBuilder(QueryBuilder inner)
    {
        _inner = inner;
    }

    public TypedQueryBuilder<T> Select(Expression<Func<T, object>> selector)
    {
        _inner.Select<T>(selector);
        return this;
    }

    public TypedQueryBuilder<T> Where(Expression<Func<T, bool>> expression)
    {
        _inner.Where<T>(expression);
        return this;
    }

    public TypedQueryBuilder<T> OrWhere(Expression<Func<T, bool>> expression)
    {
        _inner.OrWhere<T>(expression);
        return this;
    }

    public TypedQueryBuilder<T> WhereGroup(Action<TypedQueryBuilder<T>> action)
    {
        _inner.WhereGroup(q => action(new TypedQueryBuilder<T>(q)));
        return this;
    }

    public TypedQueryBuilder<T> OrWhereGroup(Action<TypedQueryBuilder<T>> action)
    {
        _inner.OrWhereGroup(q => action(new TypedQueryBuilder<T>(q)));
        return this;
    }

    public TypedQueryBuilder<T> WhereRaw(string sql)
    {
        _inner.WhereRaw(sql);
        return this;
    }

    /// <summary>LEFT JOIN 其他資料表，需指定 TJoin 型別與 ON 條件字串。</summary>
    public TypedQueryBuilder<T> LeftJoin<TJoin>(string onCondition)
    {
        _inner.LeftJoin<TJoin>(onCondition);
        return this;
    }

    /// <summary>對 JOIN 進來的資料表加 WHERE 條件，需指定 TJoin 型別。</summary>
    public TypedQueryBuilder<T> WhereJoin<TJoin>(Expression<Func<TJoin, bool>> expression)
    {
        _inner.Where<TJoin>(expression);
        return this;
    }

    public TypedQueryBuilder<T> OrderBy(Expression<Func<T, object>> expression)
    {
        _inner.OrderBy<T>(expression);
        return this;
    }

    public TypedQueryBuilder<T> OrderByDescending(Expression<Func<T, object>> expression)
    {
        _inner.OrderByDescending<T>(expression);
        return this;
    }

    public TypedQueryBuilder<T> Limit(int limit)
    {
        _inner.Limit(limit);
        return this;
    }

    public TypedQueryBuilder<T> Offset(int offset)
    {
        _inner.Offset(offset);
        return this;
    }

    public (string Sql, DynamicParameters Params) Build() => _inner.Build();

    // 隱含轉換，讓 TypedQueryBuilder<T> 可直接傳給 DbContext.QueryAsync(QueryBuilder) 等方法
    public static implicit operator QueryBuilder(TypedQueryBuilder<T> builder) => builder._inner;
}
