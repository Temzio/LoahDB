using System.Collections.Concurrent;
using LoahDB.Engine;

namespace LoahDB;

/// <summary>
/// Entry point for a multi-collection document database backed by JSON files.
/// </summary>
public sealed class LoahStore : IDisposable
{
    internal const string DefaultIdIndexName = "_id";
    private readonly ConcurrentDictionary<string, object> _collections = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _metadataPath;
    private LoahTransaction? _activeTransaction;
    private readonly LoahPageStore? _pageStore;

    public LoahStore(string root, LoahOptions? options = null)
    {
        Root = root;
        Options = options?.Clone() ?? new LoahOptions();
        Storage = new LoahStorage(Options);
        if (Options.StorageFormat == LoahStorageFormat.PageFile)
        {
            var pagePath = Path.Combine(Options.BasePath, root + ".loahdb");
            _pageStore = new LoahPageStore(pagePath, Options);
        }

        _metadataPath = Storage.ResolvePath(root, "_meta");
        EnsureMetadata();
    }

    public string Root { get; }
    public LoahOptions Options { get; }
    internal LoahStorage Storage { get; }
    internal LoahPageStore? PageStore => _pageStore;
    internal bool HasActiveTransaction => _activeTransaction is not null;

    /// <summary>
    /// Imports legacy v1 JSON collection files from <c>{root}/_collections/*.loah</c> into the page-file store.
    /// </summary>
    public void ImportLegacyV1()
    {
        if (_pageStore is null)
        {
            throw new InvalidOperationException("ImportLegacyV1 requires LoahStorageFormat.PageFile.");
        }

        var collectionsDir = Path.Combine(Options.BasePath, Root, "_collections");
        LegacyV1Importer.Import(_pageStore, collectionsDir, Options);
    }

    public LoahCollection<T> Collection<T>(string name) where T : class, ILoahDocument
    {
        var key = $"{typeof(T).FullName}::{name}";
        if (_collections.TryGetValue(key, out var existing))
        {
            return (LoahCollection<T>)existing;
        }

        var collection = new LoahCollection<T>(this, name);
        if (_collections.TryAdd(key, collection))
        {
            RegisterCollectionName(name);
            return collection;
        }

        return (LoahCollection<T>)_collections[key];
    }

    public LoahTransaction BeginTransaction() => _activeTransaction ??= new LoahTransaction(this);

    /// <summary>Returns persisted metadata for this store (schema version, collection names).</summary>
    public LoahStoreInfo GetInfo()
    {
        var meta = Storage.Read<LoahStoreMetadata>(_metadataPath) ?? new LoahStoreMetadata();
        return new LoahStoreInfo(meta.SchemaVersion, meta.CreatedAtUtc, meta.UpdatedAtUtc, meta.Collections);
    }

    public int PurgeExpired<T>(string collectionName) where T : class, ILoahExpiringDocument
    {
        var collection = Collection<T>(collectionName);
        var now = DateTime.UtcNow;
        return collection.DeleteMany(d => d.ExpiresAtUtc.HasValue && d.ExpiresAtUtc.Value <= now);
    }

    public void MigrateTo(int targetSchemaVersion, Action<int, LoahStore> migration)
    {
        var meta = Storage.Read<LoahStoreMetadata>(_metadataPath) ?? new LoahStoreMetadata();
        while (meta.SchemaVersion < targetSchemaVersion)
        {
            var from = meta.SchemaVersion;
            migration(from + 1, this);
            meta.SchemaVersion = from + 1;
            meta.UpdatedAtUtc = DateTime.UtcNow;
            Storage.Write(_metadataPath, meta);
        }

        Options.SchemaVersion = targetSchemaVersion;
    }

    internal void RegisterPendingWrite(string filePath, object payload)
    {
        if (_activeTransaction is null)
        {
            Storage.Write(filePath, payload);
            return;
        }

        _activeTransaction.StageWrite(filePath, payload);
    }

    internal void EndTransaction(LoahTransaction transaction)
    {
        if (_activeTransaction == transaction)
        {
            _activeTransaction = null;
        }
    }

    internal IEnumerable<ILoahCollectionReloadable> GetLoadedCollections() =>
        _collections.Values.OfType<ILoahCollectionReloadable>();

    private void EnsureMetadata()
    {
        var meta = Storage.Read<LoahStoreMetadata>(_metadataPath);
        if (meta is null)
        {
            meta = new LoahStoreMetadata { SchemaVersion = Options.SchemaVersion };
            Storage.Write(_metadataPath, meta);
        }
    }

    private void RegisterCollectionName(string name)
    {
        var meta = Storage.Read<LoahStoreMetadata>(_metadataPath) ?? new LoahStoreMetadata();
        if (!meta.Collections.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            meta.Collections.Add(name);
            meta.UpdatedAtUtc = DateTime.UtcNow;
            Storage.Write(_metadataPath, meta);
        }
    }

    public void Dispose()
    {
        _pageStore?.Dispose();
    }
}

internal interface ILoahCollectionReloadable
{
    void Reload();
}
