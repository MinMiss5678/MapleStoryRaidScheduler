namespace Utils.SqlBuilder;

/// <summary>
/// SqlBuilder 的靜態入口點，提供統一、易讀的 CRUD 建構器。
/// <example><code>
/// // SELECT
/// var sql = Sql.From&lt;BossDbModel&gt;()
///     .Select(x => new { x.Id, x.Name })
///     .Where(x => x.Id == bossId);
///
/// // INSERT
/// var sql = Sql.InsertInto&lt;BossDbModel&gt;()
///     .Set(x => x.Name, boss.Name)
///     .ReturnId();
///
/// // UPDATE
/// var sql = Sql.Update&lt;BossDbModel&gt;()
///     .Set(x => x.Name, boss.Name)
///     .Where(x => x.Id == boss.Id);
///
/// // DELETE
/// var sql = Sql.DeleteFrom&lt;BossDbModel&gt;()
///     .Where(x => x.Id == bossId);
/// </code></example>
/// </summary>
public static class Sql
{
    public static TypedQueryBuilder<T> From<T>() => new();
    public static InsertBuilder<T> InsertInto<T>() => new();
    public static UpdateBuilder<T> Update<T>() => new();
    public static DeleteBuilder<T> DeleteFrom<T>() => new();
}
