using System.Linq.Expressions;
using System.Reflection;

namespace LoahDB.Query;

internal sealed class ParsedBounds
{
    public string? PropertyPath { get; set; }
    public object? MinInclusive { get; set; }
    public object? MaxInclusive { get; set; }
    public object? EqualValue { get; set; }
    public Expression? Residual { get; set; }
}

internal static class ExpressionPredicateParser<T>
{
    public static Expression<Func<T, bool>>? CombineAnd(IReadOnlyList<Expression<Func<T, bool>>> predicates)
    {
        if (predicates.Count == 0)
        {
            return null;
        }

        Expression<Func<T, bool>>? combined = predicates[0];
        for (var i = 1; i < predicates.Count; i++)
        {
            var param = Expression.Parameter(typeof(T), "x");
            var left = Expression.Invoke(combined!, param);
            var right = Expression.Invoke(predicates[i], param);
            combined = Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left, right), param);
        }

        return combined;
    }

    public static bool TryExtractIndexEquality(
        Expression<Func<T, bool>> predicate,
        out string propertyPath,
        out object? value)
    {
        propertyPath = "";
        value = null;
        if (!TryParseComparison(predicate.Body, out var path, out var op, out var constant))
        {
            return false;
        }

        if (op != ComparisonOp.Equal)
        {
            return false;
        }

        propertyPath = path;
        value = constant;
        return true;
    }

    public static bool TryExtractIndexRange(
        Expression<Func<T, bool>> predicate,
        out ParsedBounds bounds)
    {
        bounds = new ParsedBounds();
        if (predicate.Body is not BinaryExpression { NodeType: ExpressionType.AndAlso } andAlso)
        {
            if (TryParseComparison(predicate.Body, out var path, out var op, out var constant))
            {
                bounds.PropertyPath = path;
                ApplyBound(bounds, op, constant);
                return bounds.PropertyPath is not null;
            }

            return false;
        }

        if (!TryParseComparison(andAlso.Left, out var leftPath, out var leftOp, out var leftConst) ||
            !TryParseComparison(andAlso.Right, out var rightPath, out var rightOp, out var rightConst) ||
            leftPath != rightPath)
        {
            return false;
        }

        bounds.PropertyPath = leftPath;
        ApplyBound(bounds, leftOp, leftConst);
        ApplyBound(bounds, rightOp, rightConst);
        return true;
    }

    public static string? TryExtractOrderPath(Expression keySelector)
    {
        if (keySelector is LambdaExpression lambda)
        {
            keySelector = lambda.Body;
        }

        try
        {
            return ExpressionPath.GetMemberPath(keySelector);
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyBound(ParsedBounds bounds, ComparisonOp op, object? constant)
    {
        switch (op)
        {
            case ComparisonOp.Equal:
                bounds.EqualValue = constant;
                break;
            case ComparisonOp.GreaterThan:
                bounds.MinInclusive = BumpMin(bounds.MinInclusive, constant, exclusive: true);
                break;
            case ComparisonOp.GreaterThanOrEqual:
                bounds.MinInclusive = BumpMin(bounds.MinInclusive, constant, exclusive: false);
                break;
            case ComparisonOp.LessThan:
                bounds.MaxInclusive = BumpMax(bounds.MaxInclusive, constant, exclusive: true);
                break;
            case ComparisonOp.LessThanOrEqual:
                bounds.MaxInclusive = BumpMax(bounds.MaxInclusive, constant, exclusive: false);
                break;
        }
    }

    private static object? BumpMin(object? current, object? candidate, bool exclusive)
    {
        if (candidate is null)
        {
            return current;
        }

        return current is null ? candidate : Comparer<object>.Default.Compare(candidate, current) > 0 ? candidate : current;
    }

    private static object? BumpMax(object? current, object? candidate, bool exclusive)
    {
        if (candidate is null)
        {
            return current;
        }

        return current is null ? candidate : Comparer<object>.Default.Compare(candidate, current) < 0 ? candidate : current;
    }

    private enum ComparisonOp
    {
        Equal,
        NotEqual,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
    }

    private static bool TryParseComparison(Expression body, out string propertyPath, out ComparisonOp op, out object? constant)
    {
        propertyPath = "";
        constant = null;
        op = ComparisonOp.Equal;

        if (body is BinaryExpression binary)
        {
            op = binary.NodeType switch
            {
                ExpressionType.Equal => ComparisonOp.Equal,
                ExpressionType.NotEqual => ComparisonOp.NotEqual,
                ExpressionType.GreaterThan => ComparisonOp.GreaterThan,
                ExpressionType.GreaterThanOrEqual => ComparisonOp.GreaterThanOrEqual,
                ExpressionType.LessThan => ComparisonOp.LessThan,
                ExpressionType.LessThanOrEqual => ComparisonOp.LessThanOrEqual,
                _ => ComparisonOp.Equal,
            };

            if (binary.NodeType is ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.GreaterThan
                or ExpressionType.GreaterThanOrEqual or ExpressionType.LessThan or ExpressionType.LessThanOrEqual)
            {
                if (TryGetMemberPath(binary.Left, out propertyPath) && TryGetConstant(binary.Right, out constant))
                {
                    return true;
                }

                if (TryGetMemberPath(binary.Right, out propertyPath) && TryGetConstant(binary.Left, out constant))
                {
                    op = InvertOp(op);
                    return true;
                }
            }
        }

        if (body is MethodCallExpression method)
        {
            if (method.Method.Name == nameof(string.StartsWith) && method.Object is not null &&
                TryGetMemberPath(method.Object, out propertyPath) && method.Arguments.Count > 0 &&
                TryGetConstant(method.Arguments[0], out constant))
            {
                op = ComparisonOp.Equal;
                return false;
            }
        }

        return false;
    }

    private static ComparisonOp InvertOp(ComparisonOp op) => op switch
    {
        ComparisonOp.GreaterThan => ComparisonOp.LessThan,
        ComparisonOp.GreaterThanOrEqual => ComparisonOp.LessThanOrEqual,
        ComparisonOp.LessThan => ComparisonOp.GreaterThan,
        ComparisonOp.LessThanOrEqual => ComparisonOp.GreaterThanOrEqual,
        _ => op,
    };

    private static bool TryGetMemberPath(Expression expression, out string path)
    {
        try
        {
            path = ExpressionPath.GetMemberPath(expression);
            return true;
        }
        catch
        {
            path = "";
            return false;
        }
    }

    private static bool TryGetConstant(Expression expression, out object? value)
    {
        if (expression is ConstantExpression constant)
        {
            value = constant.Value;
            return true;
        }

        if (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary &&
            unary.Operand is ConstantExpression inner)
        {
            value = inner.Value;
            return true;
        }

        try
        {
            var lambda = Expression.Lambda(expression);
            value = lambda.Compile().DynamicInvoke();
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }
}
