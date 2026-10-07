using System.Linq.Expressions;

namespace LoahDB.Query;

internal static class CollectionQueryExecutor<T> where T : class, ILoahDocument
{
    public static IEnumerable<T> Execute(
        LoahCollection<T> collection,
        IReadOnlyList<T> snapshot,
        QueryPlan plan,
        IReadOnlyList<Expression<Func<T, bool>>> predicates)
    {
        IEnumerable<T> sequence = snapshot;
        var combined = ExpressionPredicateParser<T>.CombineAnd(predicates);

        foreach (var step in plan.Steps)
        {
            sequence = step switch
            {
                CollectionScanStep => snapshot,
                IndexSeekStep seek when collection is not null => collection.QueryExecuteIndexSeek(seek.IndexName, seek.Value, snapshot),
                IndexRangeStep range when collection is not null => collection.QueryExecuteIndexRange(
                    range.IndexName,
                    range.MinInclusive,
                    range.MaxInclusive,
                    range.Descending,
                    snapshot),
                FilterStep => ApplyFilter(sequence, combined),
                SortStep sort => ApplySort(sequence, sort),
                SortExpressionStep sortExpr => ApplySortExpression(sequence, sortExpr),
                SkipStep skip => sequence.Skip(skip.Count),
                TakeStep take => sequence.Take(take.Count),
                _ => sequence,
            };
        }

        return sequence;
    }

    public static IEnumerable<TResult> ExecuteSelect<TResult>(
        LoahCollection<T> collection,
        IReadOnlyList<T> snapshot,
        QueryPlan plan,
        IReadOnlyList<Expression<Func<T, bool>>> predicates,
        Expression<Func<T, TResult>> selector) =>
        Execute(collection, snapshot, plan, predicates).Select(selector.Compile());

    private static IEnumerable<T> ApplyFilter(IEnumerable<T> sequence, Expression<Func<T, bool>>? combined)
    {
        if (combined is null)
        {
            return sequence;
        }

        var compiled = combined.Compile();
        return sequence.Where(compiled);
    }

    private static IEnumerable<T> ApplySortExpression(IEnumerable<T> sequence, SortExpressionStep sort)
    {
        if (sort.KeySelector is not LambdaExpression lambda)
        {
            return sequence;
        }

        var keyFunc = Expression.Lambda<Func<T, object>>(
            Expression.Convert(lambda.Body, typeof(object)),
            lambda.Parameters).Compile();
        return sort.Descending
            ? sequence.OrderByDescending(keyFunc)
            : sequence.OrderBy(keyFunc);
    }

    private static IEnumerable<T> ApplySort(IEnumerable<T> sequence, SortStep sort)
    {
        var property = typeof(T).GetProperty(sort.PropertyPath);
        if (property is null)
        {
            return sequence;
        }

        var parameter = Expression.Parameter(typeof(T), "x");
        var access = Expression.Property(parameter, property);
        var keySelector = Expression.Lambda<Func<T, object>>(Expression.Convert(access, typeof(object)), parameter).Compile();
        return sort.Descending
            ? sequence.OrderByDescending(keySelector)
            : sequence.OrderBy(keySelector);
    }
}
