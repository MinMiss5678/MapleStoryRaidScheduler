using System.Text;

namespace Utils.SqlBuilder;

// 單一條件節點（葉節點）
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

// 條件群組節點，可包含 SqlCondition 或巢狀 SqlConditionGroup
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
