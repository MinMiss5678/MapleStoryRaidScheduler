using System.Linq.Expressions;
using System.Text;
using Dapper;

namespace Utils.SqlBuilder;

public class SqlExpressionVisitor : ExpressionVisitor
{
    private readonly string? _alias;
    private readonly DynamicParameters _parameters;
    private readonly StringBuilder _sb = new();

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
        // null 比較需轉為 IS NULL / IS NOT NULL
        if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
        {
            var isNullRight = node.Right is ConstantExpression { Value: null };
            var isNullLeft = node.Left is ConstantExpression { Value: null };
            if (isNullRight || isNullLeft)
            {
                Visit(isNullRight ? node.Left : node.Right);
                _sb.Append(node.NodeType == ExpressionType.Equal ? " IS NULL" : " IS NOT NULL");
                return node;
            }
        }

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

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.Name == "Contains")
        {
            var isEnumerable = typeof(System.Collections.IEnumerable).IsAssignableFrom(node.Method.DeclaringType);
            var isStaticEnumerable = node.Method.DeclaringType == typeof(System.Linq.Enumerable);

            if (isEnumerable || isStaticEnumerable)
            {
                object? collection = null;
                var collectionExpr = node.Object ?? node.Arguments[0];

                if (collectionExpr is ConstantExpression constExpr)
                {
                    collection = constExpr.Value;
                }
                else
                {
                    collection = Expression.Lambda(collectionExpr).Compile().DynamicInvoke();
                }

                if (collection is not System.Collections.IEnumerable enumerable)
                    throw new NotSupportedException("Contains only supports IEnumerable");

                if (!enumerable.Cast<object>().Any())
                {
                    _sb.Append(" (1 = 0) ");
                    return node;
                }

                var item = node.Object != null ? node.Arguments[0] : node.Arguments[1];
                
                var elementType = collection.GetType().GetGenericArguments().FirstOrDefault()
                                  ?? collection.GetType().GetElementType()
                                  ?? typeof(object);
                
                var toArrayMethod = typeof(Enumerable)
                    .GetMethod(nameof(Enumerable.ToArray))!
                    .MakeGenericMethod(elementType);

                var typedArray = toArrayMethod.Invoke(null, new object[] { collection });

                Visit(item);
                _sb.Append(" = ANY(");

                var paramName = $"p{Guid.NewGuid():N}";
                _sb.Append($"@{paramName})");

                _parameters.Add(paramName, typedArray);

                return node;
            }
        }

        return base.VisitMethodCall(node);
    }

    private void AddParameter(string name, object value)
    {
        _sb.Append($"@{name}");
        _parameters.Add(name, value);
    }
    
    private string FormatColumn(string columnName)
        => _alias != null ? $"{_alias}.\"{columnName}\"" : $"\"{columnName}\"";
}