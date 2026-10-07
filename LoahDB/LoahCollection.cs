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
    private readonly Dictionary<string, T> _byId = new(StringComparer.Ordinal);

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

    public T? GetById(string id) => _byId.TryGetValue(id, out var doc) ? doc : null;

    /// <summary>Raised after documents are inserted, updated, or deleted (after persist).</summary>
    public event EventHandler<LoahCollectionChangedEventArgs>? Changed;

    public IReadOnlyList<T> InsertMany(IEnumerable<T> documents)
    {
        var inserted = new List<T>();
        foreach (var document in documents)
        {
            inserted.Add(PrepareInsert(document));
        }

        try
        {
            foreach (var document in inserted)
            {
                _data.Documents.Add(document);
                _byId[document.Id] = document;
            }

            RebuildIndexes();
        }
        catch
        {
            foreach (var document in inserted)
            {
                _data.Documents.Remove(document);
                _byId.Remove(document.Id);
            }

            throw;
        }

        Persist();
        OnChanged(LoahChangeKind.BulkInsert, inserted.Count);
        return inserted;
    }

    public T Insert(T document) => Insert(document, raiseChanged: true);

    private T Insert(T document, bool raiseChanged)
    {
        document = PrepareInsert(document);

        try
        {
            _data.Documents.Add(document);
            _byId[document.Id] = document;
            RebuildIndexes();
        }
        catch
        {
            _data.Documents.Remove(document);
            _byId.Remove(document.Id);
            throw;
        }

        Persist();
        if (raiseChanged)
        {
            OnChanged(LoahChangeKind.Insert, 1);
        }

        return document;
    }

    private T PrepareInsert(T document)
    {
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            document.Id = Guid.NewGuid().ToString("N");
        }

        if (_byId.ContainsKey(document.Id))
        {
            throw new InvalidOperationException($"Document with id '{document.Id}' already exists.");
        }

        ValidateUniqueIndexesForDocument(document);
        return document;
    }

    public T Update(T document)
    {
        if (!_byId.TryGetValue(document.Id, out var existing))
        {
            throw new KeyNotFoundException($"Document with id '{document.Id}' was not found.");
        }

        ValidateUniqueIndexesForDocument(document, excludeDocumentId: document.Id);

        var index = _data.Documents.IndexOf(existing);
        _data.Documents[index] = document;
        _byId[document.Id] = document;
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
        if (!_byId.TryGetValue(id, out var doc))
        {
            return false;
        }

        _data.Documents.Remove(doc);
        _byId.Remove(id);
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
            _byId.Remove(doc.Id);
        }

        RebuildIndexes();
        Persist();
        OnChanged(LoahChangeKind.Delete, matches.Count);
        return matches.Count;
    }

    public LoahQuery<T> Query() => new LoahQuery<T>(_data.Documents.ToList());

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
        RebuildIdMap();
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

    private void RebuildIdMap()
    {
        _byId.Clear();
        foreach (var doc in _data.Documents)
        {
            if (!string.IsNullOrEmpty(doc.Id))
            {
                _byId[doc.Id] = doc;
            }
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

    private void ValidateUniqueIndexesForDocument(T document, string? excludeDocumentId = null)
    {
        foreach (var definition in _data.IndexDefinitions.Where(d => d.Unique))
        {
            var key = NormalizeKey(GetPropertyValue(document, definition.PropertyName));
            if (!_data.Indexes.TryGetValue(definition.Name, out var lookup) ||
                !lookup.TryGetValue(key, out var ids))
            {
                continue;
            }

            foreach (var id in ids)
            {
                if (excludeDocumentId is not null && id == excludeDocumentId)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Unique index '{definition.Name}' violation for key '{key}'.");
            }
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
