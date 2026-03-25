using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Dapper;

namespace Utils.SqlBuilder;

public class QueryBuilder
{
    private string _from = "";
    private string _orderBy = "";
    private int? _limit;
    private int? _offset;
    private readonly List<string> _selects = new();
    private readonly List<string> _joins = new();
    private readonly SqlConditionGroup _rootGroup = new("AND", false);
    private readonly DynamicParameters _parameters = new();

    private readonly Dictionary<string, string> _aliases = new();
    private int _aliasCounter = 0;
    
    private QueryBuilder(DynamicParameters parameters, Dictionary<string, string> aliases, SqlConditionGroup group)
    {
        _parameters = parameters;
        _aliases = aliases;
        _rootGroup = group;
    }
    
    public QueryBuilder() { }

    public QueryBuilder Select<TJoin>(Expression<Func<TJoin, object>> selector, string alias = "a")
    {
        var cols = ExtractColumns(selector, alias);
        _selects.AddRange(cols);
        return this;
    }
    
    public QueryBuilder From<TFrom>()
    {
        _from = GetTableName(typeof(TFrom));
        _aliases["a"] = _from;
        return this;
    }

    public QueryBuilder LeftJoin<TJoin>(string onCondition)
    {
        var alias = NextAlias();
        var table = GetTableName(typeof(TJoin));
        _aliases[alias] = table;
        _joins.Add($"LEFT JOIN \"{table}\" AS {alias} ON {onCondition}");
        return this;
    }

    // --- Where 系列 ---
    public QueryBuilder Where<TJoin>(Expression<Func<TJoin, bool>> expression)
        => AddCondition("AND", expression);

    public QueryBuilder OrWhere<TJoin>(Expression<Func<TJoin, bool>> expression)
        => AddCondition("OR", expression);

    public QueryBuilder WhereGroup(Action<QueryBuilder> groupBuilderAction)
    {
        var group = new SqlConditionGroup("AND");
        var groupBuilder = new QueryBuilder(_parameters, _aliases, group);
        groupBuilderAction(groupBuilder);
        _rootGroup.Add(group);
        return this;
    }

    public QueryBuilder OrWhereGroup(Action<QueryBuilder> groupBuilderAction)
    {
        var group = new SqlConditionGroup("OR");
        var groupBuilder = new QueryBuilder(_parameters, _aliases, group);
        groupBuilderAction(groupBuilder);
        _rootGroup.Add(group);
        return this;
    }

    private QueryBuilder AddCondition<T>(string op, Expression<Func<T, bool>> expression)
    {
        var alias = _aliases.FirstOrDefault(x => x.Value == GetTableName(typeof(T))).Key
                    ?? _aliases.First().Key;
        var visitor = new SqlExpressionVisitor(alias, _parameters);
        var condition = visitor.Translate(expression.Body);
        _rootGroup.Add(new SqlCondition(op, condition));
        
        return this;
    }

    // --- Build ---
    public QueryBuilder OrderByDescending<T>(Expression<Func<T, object>> expression)
    {
        var member = expression.Body switch
        {
            MemberExpression me => me.Member.Name,
            UnaryExpression ue when ue.Operand is MemberExpression me => me.Member.Name,
            _ => throw new NotSupportedException("Only member expressions are supported")
        };
        _orderBy = $" ORDER BY \"{member}\" DESC";
        return this;
    }

    public QueryBuilder Limit(int limit)
    {
        _limit = limit;
        return this;
    }

    public QueryBuilder Offset(int offset)
    {
        _offset = offset;
        return this;
    }

    public (string Sql, DynamicParameters Params) Build()
    {
        var table = _from;
        var sql = $"SELECT {(_selects.Count > 0 ? string.Join(", ", _selects) : "*")} FROM \"{table}\" AS a";

        if (_joins.Count > 0)
            sql += " " + string.Join(" ", _joins);

        if (_rootGroup.Conditions.Count > 0)
            sql += $" WHERE {_rootGroup.ToSql()}";

        if (!string.IsNullOrEmpty(_orderBy))
            sql += _orderBy;

        if (_limit.HasValue)
            sql += $" LIMIT {_limit.Value}";

        if (_offset.HasValue)
            sql += $" OFFSET {_offset.Value}";

        return (sql, _parameters);
    }

    // --- Helper ---
    private string NextAlias()
    {
        return ((char)('b' + _aliasCounter++)).ToString();
    }

    private static string GetTableName(Type t)
    {
        var attr = t.GetCustomAttribute<TableAttribute>();
        return attr != null ? attr.Name : t.Name;
    }

    private static IEnumerable<string> ExtractColumns(LambdaExpression selector, string alias)
    {
        if (selector.Body is NewExpression newExp)
        {
            return newExp.Members.Select((m, i) =>
            {
                var name = m.Name;
                // 取匿名型別對應的別名
                var arg = newExp.Arguments[i];
                if (arg is MemberExpression memberArg && memberArg.Member.Name != m.Name)
                    return $"{alias}.\"{memberArg.Member.Name}\" AS \"{m.Name}\"";
                return $"{alias}.\"{name}\"";
            });
        }

        if (selector.Body is MemberExpression memberExp)
            return new[] { $"{alias}.\"{memberExp.Member.Name}\"" };
    
        throw new NotSupportedException("Unsupported select expression");
    }
}

internal class SqlCondition
{
    public string Operator { get; }
    public string Condition { get; }

    public SqlCondition(string op, string cond)
    {
        Operator = op;
        Condition = cond;
    }
}

internal class SqlConditionGroup
{
    public string Operator { get; }
    public bool IsGroup { get; }
    public List<object> Conditions { get; } = new(); // SqlCondition 或 SqlConditionGroup

    public SqlConditionGroup(string op = "AND", bool isGroup = true)
    {
        Operator = op;
        IsGroup = isGroup;
    }

    public void Add(object condition) => Conditions.Add(condition);

    public string ToSql()
    {
        if (!Conditions.Any()) return "";

        var sb = new StringBuilder();
        bool first = true;
        foreach (var cond in Conditions)
        {
            if (!first)
            {
                // 根據條件自己的 Operator 串接
                sb.Append(" " + (cond is SqlCondition sc ? sc.Operator : Operator) + " ");
            }

            sb.Append(cond switch
            {
                SqlCondition sc => sc.Condition,
                SqlConditionGroup sg => sg.ToSql(),
                _ => throw new NotSupportedException()
            });

            first = false;
        }

        return IsGroup && Conditions.Count > 1 ? $"({sb})" : sb.ToString();
    }
}