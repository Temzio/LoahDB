using Newtonsoft.Json;
using System.Text;

namespace LoahDB.Engine;

internal sealed class LoahPageStore : IDisposable
{
    private readonly LoahPageDatabase _database;
    private readonly JsonSerializerSettings _jsonSettings;
    private readonly Dictionary<string, CollectionCatalogEntry> _catalogCache = new(StringComparer.OrdinalIgnoreCase);

    public LoahPageStore(string filePath, LoahOptions options)
    {
        _database = new LoahPageDatabase(filePath, options.PageCacheCapacity);
        _jsonSettings = options.SerializerSettings;
        LoadCatalogCache();
    }

    public LoahPageDatabase Database => _database;

    public BPlusTree OpenCollectionTree(string collectionName)
    {
        var entry = GetOrCreateCatalogEntry(collectionName);
        return _database.OpenTree(entry.RootPageId);
    }

    public CollectionCatalogEntry GetOrCreateCatalogEntry(string collectionName)
    {
        if (_catalogCache.TryGetValue(collectionName, out var cached))
        {
            return cached;
        }

        var catalog = _database.OpenCatalog();
        if (catalog.TryGet(collectionName, out var bytes))
        {
            var json = Encoding.UTF8.GetString(bytes);
            var entry = JsonConvert.DeserializeObject<CollectionCatalogEntry>(json, _jsonSettings)
                        ?? new CollectionCatalogEntry();
            _catalogCache[collectionName] = entry;
            return entry;
        }

        var tree = _database.CreateTree();
        var created = new CollectionCatalogEntry { RootPageId = tree.RootPageId };
        _catalogCache[collectionName] = created;
        SaveCatalogEntry(collectionName, created);
        return created;
    }

    public void SaveCatalogEntry(string collectionName, CollectionCatalogEntry entry)
    {
        entry.UpdatedAtUtc = DateTime.UtcNow;
        _catalogCache[collectionName] = entry;
        var json = JsonConvert.SerializeObject(entry, _jsonSettings);
        var catalog = _database.OpenCatalog();
        catalog.Insert(collectionName, Encoding.UTF8.GetBytes(json));
    }

    public void Dispose() => _database.Dispose();

    private void LoadCatalogCache()
    {
        foreach (var (key, value) in _database.OpenCatalog().Scan())
        {
            var json = Encoding.UTF8.GetString(value);
            var entry = JsonConvert.DeserializeObject<CollectionCatalogEntry>(json, _jsonSettings);
            if (entry is not null)
            {
                _catalogCache[key] = entry;
            }
        }
    }
}
