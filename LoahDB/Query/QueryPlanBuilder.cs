using System.Linq.Expressions;

namespace LoahDB.Query;

internal static class QueryPlanBuilder<T> where T : class
{
    public static QueryPlan Build(
        IReadOnlyList<LoahIndexDefinition> indexes,
        IReadOnlyList<Expression<Func<T, bool>>> predicates,
        IReadOnlyList<(Expression KeySelector, bool Descending)> orderings,
        int? skip,
        int? take,
        Expression? projection,
        Expression? groupByKey)
    {
        var plan = new QueryPlan();
        var combined = ExpressionPredicateParser<T>.CombineAnd(predicates);
        var usedIndex = false;

        if (combined is not null &&
            ExpressionPredicateParser<T>.TryExtractIndexEquality(combined, out var eqPath, out var eqValue))
        {
            var index = FindSingleFieldIndex(indexes, eqPath);
            if (index is not null)
            {
                plan.Steps.Add(new IndexSeekStep(index.Name, eqPath, eqValue));
                usedIndex = true;
            }
        }
        else if (combined is not null &&
                 ExpressionPredicateParser<T>.TryExtractIndexRange(combined, out var bounds) &&
                 bounds.EqualValue is null)
        {
            var index = FindSingleFieldIndex(indexes, bounds.PropertyPath!);
            if (index is not null)
            {
                plan.Steps.Add(new IndexRangeStep(
                    index.Name,
                    bounds.PropertyPath!,
                    bounds.MinInclusive,
                    bounds.MaxInclusive,
                    false));
                usedIndex = true;
            }
        }

        if (!usedIndex)
        {
            plan.Steps.Add(new CollectionScanStep());
        }

        if (combined is not null && !usedIndex)
        {
            plan.Steps.Add(new FilterStep(combined));
        }

        if (orderings.Count > 0)
        {
            var (key, desc) = orderings[0];
            var path = ExpressionPredicateParser<T>.TryExtractOrderPath(key);
            if (path is not null)
            {
                var index = FindSingleFieldIndex(indexes, path);
                if (index is not null && plan.Steps.LastOrDefault() is IndexRangeStep range &&
                    range.PropertyPath == path)
                {
                    plan.Steps[^1] = range with { Descending = desc };
                }
                else if (index is not null && !usedIndex)
                {
                    plan.Steps.Clear();
                    plan.Steps.Add(new IndexRangeStep(index.Name, path, null, null, desc));
                    usedIndex = true;
                }
                else
                {
                    plan.Steps.Add(new SortExpressionStep(key, desc));
                }
            }
            else
            {
                plan.Steps.Add(new SortExpressionStep(key, orderings[0].Descending));
            }
        }

        if (groupByKey is not null)
        {
            plan.Steps.Add(new GroupByStep(groupByKey));
        }

        if (projection is not null)
        {
            plan.Steps.Add(new ProjectStep(projection));
        }

        if (skip is int s)
        {
            plan.Steps.Add(new SkipStep(s));
        }

        if (take is int t)
        {
            plan.Steps.Add(new TakeStep(t));
        }

        return plan;
    }

    private static LoahIndexDefinition? FindSingleFieldIndex(IReadOnlyList<LoahIndexDefinition> indexes, string propertyPath)
    {
        return indexes.FirstOrDefault(i =>
            i.Name != LoahStore.DefaultIdIndexName &&
            (i.GetPaths().Count == 1 && i.GetPaths()[0] == propertyPath ||
             i.PropertyName == propertyPath && i.PropertyPaths.Count == 0));
    }
}
