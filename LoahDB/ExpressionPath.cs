using System.Linq.Expressions;

namespace LoahDB;

internal static class ExpressionPath
{
    public static string GetMemberPath(Expression expression)
    {
        if (expression is LambdaExpression lambda)
        {
            return GetMemberPath(lambda.Body);
        }

        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            return GetMemberPath(unary.Operand);
        }

        if (expression is not MemberExpression member)
        {
            throw new ArgumentException("Index key must be a property access expression.", nameof(expression));
        }

        var parts = new Stack<string>();
        Expression? current = member;
        while (current is MemberExpression m)
        {
            parts.Push(m.Member.Name);
            current = m.Expression;
        }

        if (current is not ParameterExpression)
        {
            throw new ArgumentException("Index key must be a simple property access chain.", nameof(expression));
        }

        return string.Join('.', parts);
    }
}
