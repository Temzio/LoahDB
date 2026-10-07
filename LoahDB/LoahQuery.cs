using System.Linq.Expressions;

namespace LoahDB;

/// <summary>
/// Fluent, in-memory query over a loaded collection snapshot.
/// </summary>
public sealed class LoahQuery<T>
{
    private readonly IEnumerable<T> _source;
    private readonly List<Func<T, bool>> _filters = new();
    private readonly List<(bool Desc, Func<T, object> Key)> _orderings = new();
    private int? _skip;
    private int? _take;

    internal LoahQuery(IEnumerable<T> source) => _source = source;

    public LoahQuery<T> Where(Expression<Func<T, bool>> predicate)
    {
        var compiled = predicate.Compile();
        _filters.Add(compiled);
        return this;
    }

    public LoahQuery<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        var compiled = keySelector.Compile();
        _orderings.Add((false, o => compiled(o)!));
        return this;
    }

    public LoahQuery<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        var compiled = keySelector.Compile();
        _orderings.Add((true, o => compiled(o)!));
        return this;
    }

    public LoahQuery<T> Skip(int count)
    {
        _skip = count;
        return this;
    }

    public LoahQuery<T> Take(int count)
    {
        _take = count;
        return this;
    }

    public List<T> ToList()
    {
        IEnumerable<T> query = _source;
        foreach (var filter in _filters)
        {
            query = query.Where(filter);
        }

        if (_orderings.Count > 0)
        {
            IOrderedEnumerable<T>? ordered = null;
            foreach (var (desc, key) in _orderings)
            {
                ordered = ordered is null
                    ? (desc ? query.OrderByDescending(key) : query.OrderBy(key))
                    : (desc ? ordered.ThenByDescending(key) : ordered.ThenBy(key));
            }

            query = ordered ?? query;
        }

        if (_skip is int skip)
        {
            query = query.Skip(skip);
        }

        if (_take is int take)
        {
            query = query.Take(take);
        }

        return query.ToList();
    }

    public T? FirstOrDefault() => ToList().FirstOrDefault();

    public int Count() => ToList().Count;
}
