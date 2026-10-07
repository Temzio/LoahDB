using System.Linq.Expressions;
using LoahDB.Query;

namespace LoahDB;

/// <summary>
/// Fluent query over a collection snapshot with index-aware planning.
/// </summary>
public sealed class LoahQuery<T> where T : class, ILoahDocument
{
    private readonly LoahCollection<T> _collection;
    private readonly IReadOnlyList<T> _snapshot;
    private readonly List<Expression<Func<T, bool>>> _predicates = new();
    private readonly List<(Expression KeySelector, bool Descending)> _orderings = new();
    private int? _skip;
    private int? _take;
    private QueryPlan? _cachedPlan;

    internal LoahQuery(LoahCollection<T> collection, IReadOnlyList<T> snapshot)
    {
        _collection = collection;
        _snapshot = snapshot;
    }

    /// <summary>Snapshot-only queries (tests); index planning is disabled.</summary>
    internal LoahQuery(IReadOnlyList<T> snapshot)
    {
        _collection = null!;
        _snapshot = snapshot;
    }

    public LoahQuery<T> Where(Expression<Func<T, bool>> predicate)
    {
        _predicates.Add(predicate);
        _cachedPlan = null;
        return this;
    }

    public LoahQuery<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        _orderings.Add((keySelector, false));
        _cachedPlan = null;
        return this;
    }

    public LoahQuery<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        _orderings.Add((keySelector, true));
        _cachedPlan = null;
        return this;
    }

    public LoahQuery<T> Skip(int count)
    {
        _skip = count;
        _cachedPlan = null;
        return this;
    }

    public LoahQuery<T> Take(int count)
    {
        _take = count;
        _cachedPlan = null;
        return this;
    }

    public LoahProjectedQuery<T, TResult> Select<TResult>(Expression<Func<T, TResult>> selector) =>
        new(this, selector);

    public LoahGroupedQuery<T, TKey> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector) =>
        new(this, keySelector);

    public List<TResult> Join<TInner, TKey, TResult>(
        LoahCollection<TInner> inner,
        Expression<Func<T, TKey>> outerKeySelector,
        Expression<Func<TInner, TKey>> innerKeySelector,
        Expression<Func<T, TInner, TResult>> resultSelector) where TInner : class, ILoahDocument
    {
        var outerPath = ExpressionPath.GetMemberPath(outerKeySelector);
        var innerPath = ExpressionPath.GetMemberPath(innerKeySelector);
        var outerKey = outerKeySelector.Compile();
        var innerKey = innerKeySelector.Compile();
        var result = resultSelector.Compile();
        var innerIndex = inner.FindIndexForPropertyPath(innerPath);

        var plan = GetPlan();
        plan.Steps.Add(new JoinStep(inner.Name, outerPath, innerPath, innerIndex is not null));

        var results = new List<TResult>();
        foreach (var outer in ExecuteDocuments())
        {
            var key = outerKey(outer);
            IEnumerable<TInner> inners = innerIndex is not null
                ? inner.QueryExecuteIndexSeek(innerIndex.Name, key!, inner.SnapshotForQuery())
                : inner.SnapshotForQuery();
            foreach (var innerDoc in inners.Where(i => EqualityComparer<TKey>.Default.Equals(innerKey(i), key)))
            {
                results.Add(result(outer, innerDoc));
            }
        }

        return results;
    }

    public string Explain() => GetPlan().Explain();

    public List<T> ToList() => ExecuteDocuments().ToList();

    public T? FirstOrDefault() => ExecuteDocuments().FirstOrDefault();

    public int Count() => ExecuteDocuments().Count();

    public bool Any() => ExecuteDocuments().Any();

    public int Sum(Expression<Func<T, int>> selector) => ExecuteDocuments().Sum(selector.Compile());

    public long Sum(Expression<Func<T, long>> selector) => ExecuteDocuments().Sum(selector.Compile());

    public double Sum(Expression<Func<T, double>> selector) => ExecuteDocuments().Sum(selector.Compile());

    public int? Min(Expression<Func<T, int>> selector) => ExecuteDocuments().Select(selector.Compile()).DefaultIfEmpty().Min();

    public int? Max(Expression<Func<T, int>> selector) => ExecuteDocuments().Select(selector.Compile()).DefaultIfEmpty().Max();

    public double? Min(Expression<Func<T, double>> selector) => ExecuteDocuments().Select(selector.Compile()).DefaultIfEmpty().Min();

    public double? Max(Expression<Func<T, double>> selector) => ExecuteDocuments().Select(selector.Compile()).DefaultIfEmpty().Max();

    public double Average(Expression<Func<T, int>> selector) => ExecuteDocuments().Select(selector.Compile()).DefaultIfEmpty().Average();

    public double Average(Expression<Func<T, double>> selector) => ExecuteDocuments().Select(selector.Compile()).DefaultIfEmpty().Average();

    internal IEnumerable<T> ExecuteDocuments() =>
        CollectionQueryExecutor<T>.Execute(_collection, _snapshot, GetPlan(), _predicates);

    internal QueryPlan GetPlan() =>
        _cachedPlan ??= _collection is null
            ? BuildSnapshotOnlyPlan()
            : QueryPlanBuilder<T>.Build(
                _collection.QueryIndexDefinitions(),
                _predicates,
                _orderings,
                _skip,
                _take,
                null,
                null);

    private QueryPlan BuildSnapshotOnlyPlan()
    {
        var plan = new QueryPlan { Steps = { new CollectionScanStep() } };
        if (_predicates.Count > 0)
        {
            var combined = ExpressionPredicateParser<T>.CombineAnd(_predicates);
            if (combined is not null)
            {
                plan.Steps.Add(new FilterStep(combined));
            }
        }

        foreach (var (key, desc) in _orderings)
        {
            plan.Steps.Add(new SortExpressionStep(key, desc));
        }

        if (_skip is int skip)
        {
            plan.Steps.Add(new SkipStep(skip));
        }

        if (_take is int take)
        {
            plan.Steps.Add(new TakeStep(take));
        }

        return plan;
    }
}

public sealed class LoahProjectedQuery<T, TResult> where T : class, ILoahDocument
{
    private readonly LoahQuery<T> _source;
    private readonly Expression<Func<T, TResult>> _selector;

    internal LoahProjectedQuery(LoahQuery<T> source, Expression<Func<T, TResult>> selector)
    {
        _source = source;
        _selector = selector;
    }

    public List<TResult> ToList() => _source.ExecuteDocuments().Select(_selector.Compile()).ToList();

    public TResult? FirstOrDefault() => ToList().FirstOrDefault();

    public int Count() => ToList().Count;
}

public sealed class LoahGroupedQuery<T, TKey> where T : class, ILoahDocument
{
    private readonly LoahQuery<T> _source;
    private readonly Expression<Func<T, TKey>> _keySelector;

    internal LoahGroupedQuery(LoahQuery<T> source, Expression<Func<T, TKey>> keySelector)
    {
        _source = source;
        _keySelector = keySelector;
    }

    public List<IGrouping<TKey, T>> ToList() =>
        _source.ExecuteDocuments().GroupBy(_keySelector.Compile()).ToList();
}
