using System.Linq.Expressions;
using System.Reflection;

namespace LoahDB;

/// <summary>
/// A named collection of documents persisted as a single JSON file.
/// </summary>
public sealed class LoahCollection<T> : ILoahCollectionReloadable where T : class, ILoahDocument
{
    private readonly LoahStore _store;
    private readonly string _name;
    private readonly LoahStorage _storage;
    private readonly string _filePath;
    private LoahCollectionData<T> _data = new();

    internal LoahCollection(LoahStore store, string name)
    {
        _store = store;
        _name = name;
        _storage = store.Storage;
        _filePath = _storage.ResolveCollectionPath(store.Root, name);
        Reload();
    }

    public string Name => _name;

    public IReadOnlyList<T> All() => _data.Documents.ToList();

    public int Count => _data.Documents.Count;

    public T? GetById(string id) => _data.Documents.FirstOrDefault(d => d.Id == id);

    /// <summary>Raised after documents are inserted, updated, or deleted (after persist).</summary>
    public event EventHandler<LoahCollectionChangedEventArgs>? Changed;

    public IReadOnlyList<T> InsertMany(IEnumerable<T> documents)
    {
        var inserted = new List<T>();
        foreach (var document in documents)
        {
            inserted.Add(Insert(document, raiseChanged: false));
        }

        OnChanged(LoahChangeKind.BulkInsert, inserted.Count);
        return inserted;
    }

    public T Insert(T document) => Insert(document, raiseChanged: true);

    private T Insert(T document, bool raiseChanged)
    {
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            document.Id = Guid.NewGuid().ToString("N");
        }

        if (_data.Documents.Any(d => d.Id == document.Id))
        {
            throw new InvalidOperationException($"Document with id '{document.Id}' already exists.");
        }

        _data.Documents.Add(document);
        RebuildIndexes();
        Persist();
        if (raiseChanged)
        {
            OnChanged(LoahChangeKind.Insert, 1);
        }

        return document;
    }

    public T Update(T document)
    {
        var index = _data.Documents.FindIndex(d => d.Id == document.Id);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Document with id '{document.Id}' was not found.");
        }

        _data.Documents[index] = document;
        RebuildIndexes();
        Persist();
        OnChanged(LoahChangeKind.Update, 1);
        return document;
    }

    public T Upsert(T document)
    {
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            document.Id = Guid.NewGuid().ToString("N");
        }

        return GetById(document.Id) is null ? Insert(document) : Update(document);
    }

    public bool Delete(string id)
    {
        var doc = GetById(id);
        if (doc is null)
        {
            return false;
        }

        _data.Documents.Remove(doc);
        RebuildIndexes();
        Persist();
        OnChanged(LoahChangeKind.Delete, 1);
        return true;
    }

    public int DeleteMany(Expression<Func<T, bool>> predicate)
    {
        var matches = _data.Documents.Where(predicate.Compile()).ToList();
        if (matches.Count == 0)
        {
            return 0;
        }

        foreach (var doc in matches)
        {
            _data.Documents.Remove(doc);
        }

        RebuildIndexes();
        Persist();
        OnChanged(LoahChangeKind.Delete, matches.Count);
        return matches.Count;
    }

    public LoahQuery<T> Query() => new LoahQuery<T>(_data.Documents);

    public List<T> Find(Expression<Func<T, bool>> predicate) =>
        Query().Where(predicate).ToList();

    public T? FindOne(Expression<Func<T, bool>> predicate) =>
        Query().Where(predicate).FirstOrDefault();

    public void EnsureIndex<TKey>(string indexName, Expression<Func<T, TKey>> keySelector, bool unique = false)
    {
        if (keySelector.Body is not MemberExpression member)
        {
            throw new ArgumentException("Index key must be a simple property access.", nameof(keySelector));
        }

        var definition = _data.IndexDefinitions.FirstOrDefault(i =>
            i.Name.Equals(indexName, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
        {
            definition = new LoahIndexDefinition();
            _data.IndexDefinitions.Add(definition);
        }

        definition.Name = indexName;
        definition.PropertyName = member.Member.Name;
        definition.Unique = unique;
        RebuildIndexes();
        Persist();
    }

    public T? FindByIndex<TKey>(string indexName, TKey key)
    {
        var normalized = NormalizeKey(key);
        if (!_data.Indexes.TryGetValue(indexName, out var lookup) ||
            !lookup.TryGetValue(normalized, out var ids) ||
            ids.Count == 0)
        {
            return null;
        }

        return GetById(ids[0]);
    }

    public void Reload()
    {
        _data = _storage.Read<LoahCollectionData<T>>(_filePath) ?? new LoahCollectionData<T> { Name = _name };
        if (string.IsNullOrEmpty(_data.Name))
        {
            _data.Name = _name;
        }

        EnsureDefaultIdIndexDefinition();
        RebuildIndexes();
    }

    internal void Persist()
    {
        _data.UpdatedAtUtc = DateTime.UtcNow;
        if (_store.HasActiveTransaction)
        {
            _store.RegisterPendingWrite(_filePath, _data);
        }
        else
        {
            _storage.Write(_filePath, _data);
        }
    }

    private void EnsureDefaultIdIndexDefinition()
    {
        if (_data.IndexDefinitions.All(i => i.Name != LoahStore.DefaultIdIndexName))
        {
            _data.IndexDefinitions.Insert(0, new LoahIndexDefinition
            {
                Name = LoahStore.DefaultIdIndexName,
                PropertyName = nameof(ILoahDocument.Id),
                Unique = true,
            });
        }
    }

    private void RebuildIndexes()
    {
        _data.Indexes.Clear();
        foreach (var definition in _data.IndexDefinitions)
        {
            var lookup = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var doc in _data.Documents)
            {
                var key = NormalizeKey(GetPropertyValue(doc, definition.PropertyName));
                if (!lookup.TryGetValue(key, out var ids))
                {
                    ids = new List<string>();
                    lookup[key] = ids;
                }

                if (definition.Unique && ids.Count > 0 && !ids.Contains(doc.Id))
                {
                    throw new InvalidOperationException(
                        $"Unique index '{definition.Name}' violation for key '{key}'.");
                }

                if (!ids.Contains(doc.Id))
                {
                    ids.Add(doc.Id);
                }
            }

            _data.Indexes[definition.Name] = lookup;
        }
    }

    private static object? GetPropertyValue(T document, string propertyName)
    {
        var property = typeof(T).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null)
        {
            throw new InvalidOperationException($"Property '{propertyName}' was not found on '{typeof(T).Name}'.");
        }

        return property.GetValue(document);
    }

    private static string NormalizeKey<TKey>(TKey key) =>
        key?.ToString() ?? string.Empty;

    private void OnChanged(LoahChangeKind kind, int affectedCount) =>
        Changed?.Invoke(this, new LoahCollectionChangedEventArgs(kind, affectedCount));
}
