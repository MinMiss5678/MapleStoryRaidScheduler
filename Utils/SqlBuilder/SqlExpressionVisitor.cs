using System.Linq.Expressions;
using System.Text;
using Dapper;

namespace Utils.SqlBuilder;

public class SqlExpressionVisitor : ExpressionVisitor
{
    private readonly string? _alias;
    private readonly DynamicParameters _parameters;
    private readonly StringBuilder _sb = new();
    private int _paramIndex = 0;

    public SqlExpressionVisitor(string? alias, DynamicParameters parameters)
    {
        _alias = alias;
        _parameters = parameters;
    }

    public string Translate(Expression expression)
    {
        Visit(expression);
        return _sb.ToString();
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        Visit(node.Left);

        _sb.Append(node.NodeType switch
        {
            ExpressionType.Equal => " = ",
            ExpressionType.NotEqual => " <> ",
            ExpressionType.GreaterThan => " > ",
            ExpressionType.LessThan => " < ",
            ExpressionType.GreaterThanOrEqual => " >= ",
            ExpressionType.LessThanOrEqual => " <= ",
            ExpressionType.AndAlso => " AND ",
            ExpressionType.OrElse => " OR ",
            _ => throw new NotSupportedException($"Unsupported node: {node.NodeType}")
        });

        Visit(node.Right);
        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression != null && node.Expression.NodeType == ExpressionType.Parameter)
        {
            _sb.Append(FormatColumn(node.Member.Name));
            return node;
        }

        object? value;
        if (node.Expression is ConstantExpression constExpr)
        {
            var container = constExpr.Value;
            var member = node.Member;
            if (member is System.Reflection.FieldInfo field)
            {
                value = field.GetValue(container);
            }
            else if (member is System.Reflection.PropertyInfo prop)
            {
                value = prop.GetValue(container);
            }
            else
            {
                value = Expression.Lambda(node).Compile().DynamicInvoke();
            }
        }
        else
        {
            value = Expression.Lambda(node).Compile().DynamicInvoke();
        }

        AddParameter(node.Member.Name, value);
        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        AddParameter($"c{Guid.NewGuid():N}", node.Value);
        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Convert || node.NodeType == ExpressionType.ConvertChecked)
        {
            // 強制轉型，取裡面的值
            if (node.Operand is ConstantExpression constExp)
            {
                AddParameter($"c{Guid.NewGuid():N}", Convert.ChangeType(constExp.Value, node.Type));
                return node;
            }

            if (node.Operand is MemberExpression memberExp)
            {
                if (memberExp.Expression is ParameterExpression)
                {
                    // 參數欄位，不能 Compile
                    Visit(memberExp);
                    return node;
                }
                
                var value = Expression.Lambda(memberExp).Compile().DynamicInvoke();
                value = Convert.ChangeType(value, Nullable.GetUnderlyingType(node.Type) ?? node.Type);
                AddParameter(memberExp.Member.Name, Convert.ChangeType(value, node.Type));
                return node;
            }

            Visit(node.Operand); // fallback
            return node;
        }

        return base.VisitUnary(node);
    }

    private void AddParameter(string name, object value)
    {
        _sb.Append($"@{name}");
        _parameters.Add(name, value);
    }
    
    private string FormatColumn(string columnName)
        => _alias != null ? $"{_alias}.\"{columnName}\"" : $"\"{columnName}\"";
}