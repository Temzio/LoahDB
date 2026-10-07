using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using LoahDB.Engine;
using Newtonsoft.Json;

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
    private readonly LoahPageStore? _pageStore;
    private int _documentCount;
    private uint _treeRootPageId;

    internal LoahCollection(LoahStore store, string name)
    {
        _store = store;
        _name = name;
        _storage = store.Storage;
        _filePath = _storage.ResolveCollectionPath(store.Root, name);
        _pageStore = store.PageStore;
        Reload();
    }

    public string Name => _name;

    public IReadOnlyList<T> All() => EnumerateDocuments().ToList();

    public int Count => _pageStore is null ? _data.Documents.Count : _documentCount;

    public T? GetById(string id)
    {
        if (_byId.TryGetValue(id, out var cached))
        {
            return cached;
        }

        if (_pageStore is not null && GetTree().TryGet(id, out var bytes))
        {
            var doc = Deserialize(bytes);
            _byId[id] = doc;
            return doc;
        }

        return null;
    }

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
            if (_pageStore is not null)
            {
                var tree = GetTree();
                foreach (var document in inserted)
                {
                    tree.Insert(document.Id, Serialize(document));
                    _treeRootPageId = tree.RootPageId;
                    _byId[document.Id] = document;
                }

                _documentCount += inserted.Count;
            }
            else
            {
                foreach (var document in inserted)
                {
                    _data.Documents.Add(document);
                    _byId[document.Id] = document;
                }
            }

            RebuildIndexes();
        }
        catch
        {
            if (_pageStore is not null)
            {
                foreach (var document in inserted)
                {
                    GetTree().Delete(document.Id);
                    _byId.Remove(document.Id);
                }

                _documentCount -= inserted.Count;
            }
            else
            {
                foreach (var document in inserted)
                {
                    _data.Documents.Remove(document);
                    _byId.Remove(document.Id);
                }
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
            if (_pageStore is not null)
            {
                var tree = GetTree();
                tree.Insert(document.Id, Serialize(document));
                _treeRootPageId = tree.RootPageId;
                _byId[document.Id] = document;
                _documentCount++;
            }
            else
            {
                _data.Documents.Add(document);
                _byId[document.Id] = document;
            }

            RebuildIndexes();
        }
        catch
        {
            if (_pageStore is not null)
            {
                GetTree().Delete(document.Id);
                _byId.Remove(document.Id);
                _documentCount--;
            }
            else
            {
                _data.Documents.Remove(document);
                _byId.Remove(document.Id);
            }

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

        if (_pageStore is not null)
        {
            if (GetTree().TryGet(document.Id, out _))
            {
                throw new InvalidOperationException($"Document with id '{document.Id}' already exists.");
            }
        }
        else if (_byId.ContainsKey(document.Id))
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

        if (_pageStore is not null)
        {
            var tree = GetTree();
            tree.Insert(document.Id, Serialize(document));
            _treeRootPageId = tree.RootPageId;
            _byId[document.Id] = document;
        }
        else
        {
            var index = _data.Documents.IndexOf(existing);
            _data.Documents[index] = document;
            _byId[document.Id] = document;
        }

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

        if (_pageStore is not null)
        {
            GetTree().Delete(id);
            _documentCount--;
        }
        else
        {
            _data.Documents.Remove(doc);
        }

        _byId.Remove(id);
        RebuildIndexes();
        Persist();
        OnChanged(LoahChangeKind.Delete, 1);
        return true;
    }

    public int DeleteMany(Expression<Func<T, bool>> predicate)
    {
        var matches = EnumerateDocuments().Where(predicate.Compile()).ToList();
        if (matches.Count == 0)
        {
            return 0;
        }

        foreach (var doc in matches)
        {
            if (_pageStore is not null)
            {
                GetTree().Delete(doc.Id);
            }
            else
            {
                _data.Documents.Remove(doc);
            }

            _byId.Remove(doc.Id);
        }

        if (_pageStore is not null)
        {
            _documentCount -= matches.Count;
        }

        RebuildIndexes();
        Persist();
        OnChanged(LoahChangeKind.Delete, matches.Count);
        return matches.Count;
    }

    public LoahQuery<T> Query() => new LoahQuery<T>(EnumerateDocuments().ToList());

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
        if (_pageStore is not null)
        {
            var entry = _pageStore.GetOrCreateCatalogEntry(_name);
            _data = new LoahCollectionData<T>
            {
                Name = _name,
                IndexDefinitions = entry.IndexDefinitions,
            };
            _byId.Clear();
            _treeRootPageId = entry.RootPageId;
            _documentCount = GetTree().Scan().Count();
            EnsureDefaultIdIndexDefinition();
            RebuildIndexes();
            return;
        }

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
        if (_pageStore is not null)
        {
            var entry = _pageStore.GetOrCreateCatalogEntry(_name);
            entry.IndexDefinitions = _data.IndexDefinitions;
            entry.RootPageId = _treeRootPageId;
            _pageStore.SaveCatalogEntry(_name, entry);
            return;
        }

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
            foreach (var doc in EnumerateDocuments())
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

    private IEnumerable<T> EnumerateDocuments()
    {
        if (_pageStore is null)
        {
            return _data.Documents;
        }

        return GetTree().Scan().Select(pair => Deserialize(pair.Value));
    }

    private BPlusTree GetTree() => _pageStore!.Database.OpenTree(_treeRootPageId);

    private byte[] Serialize(T document) =>
        Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(document, _store.Options.SerializerSettings));

    private T Deserialize(byte[] bytes) =>
        JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes), _store.Options.SerializerSettings)
        ?? throw new InvalidDataException("Failed to deserialize document.");
}
