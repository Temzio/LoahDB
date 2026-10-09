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
    private readonly ConcurrentDictionary<string, ILoahReferenceQueryable> _referenceQueryables =
        new(StringComparer.OrdinalIgnoreCase);
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

    public LoahTransaction BeginTransaction()
    {
        if (_activeTransaction is not null)
        {
            throw new InvalidOperationException("A transaction is already active on this store. Commit or roll back before starting another.");
        }

        _pageStore?.BeginTransaction(Options.LockTimeout);
        return _activeTransaction = new LoahTransaction(this);
    }

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
        if (meta.SchemaVersion >= targetSchemaVersion)
        {
            Options.SchemaVersion = meta.SchemaVersion;
            return;
        }

        using var tx = BeginTransaction();
        try
        {
            while (meta.SchemaVersion < targetSchemaVersion)
            {
                var from = meta.SchemaVersion;
                migration(from + 1, this);
                meta.SchemaVersion = from + 1;
                meta.UpdatedAtUtc = DateTime.UtcNow;
                RegisterPendingWrite(_metadataPath, meta);
            }

            tx.Commit();
            Options.SchemaVersion = targetSchemaVersion;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Verifies page-file structure and optional reference constraints.</summary>
    public LoahIntegrityReport CheckIntegrity()
    {
        var report = _pageStore?.CheckIntegrity() ?? new LoahIntegrityReport();
        ValidateReferenceIntegrity(report);
        return report;
    }

    /// <summary>Flushes the page store and checkpoints the WAL for online backup.</summary>
    public void CheckpointForBackup() => _pageStore?.CheckpointForBackup();

    /// <summary>Rewrites the page-file database to drop free pages.</summary>
    public void Vacuum() => _pageStore?.Vacuum();

    /// <summary>Encrypts all page-file payloads using <see cref="LoahOptions.EncryptionKey"/>.</summary>
    public void EnableEncryption()
    {
        if (_pageStore is null)
        {
            throw new InvalidOperationException("EnableEncryption requires LoahStorageFormat.PageFile.");
        }

        if (string.IsNullOrEmpty(Options.EncryptionKey))
        {
            throw new LoahEncryptionException("Set LoahOptions.EncryptionKey before calling EnableEncryption.");
        }

        var meta = Storage.Read<LoahStoreMetadata>(_metadataPath) ?? new LoahStoreMetadata();
        using var tx = BeginTransaction();
        _pageStore.EnableEncryption(Options.EncryptionKey);
        Storage.Write(_metadataPath, meta);
        tx.Commit();
    }

    /// <summary>Re-encrypts the page-file database with a new passphrase.</summary>
    public void RotateEncryptionKey(string newEncryptionKey)
    {
        if (_pageStore is null)
        {
            throw new InvalidOperationException("RotateEncryptionKey requires LoahStorageFormat.PageFile.");
        }

        var meta = Storage.Read<LoahStoreMetadata>(_metadataPath) ?? new LoahStoreMetadata();
        using var tx = BeginTransaction();
        _pageStore.RotateEncryptionKey(newEncryptionKey);
        Options.EncryptionKey = newEncryptionKey;
        Storage.Write(_metadataPath, meta);
        tx.Commit();
    }

    internal void RegisterReferenceQueryable(ILoahReferenceQueryable queryable) =>
        _referenceQueryables[queryable.CollectionName] = queryable;

    internal bool TryResolveReference(string referencedCollection, string referencedField, string key)
    {
        if (!_referenceQueryables.TryGetValue(referencedCollection, out var target))
        {
            return false;
        }

        return target.ExistsFieldValue(referencedField, key);
    }

    internal void EnforceReferentialActionsOnParentDelete(string parentCollectionName, object parentDocument)
    {
        foreach (var child in _referenceQueryables.Values)
        {
            var schema = child.Schema;
            if (schema is null)
            {
                continue;
            }

            foreach (var reference in schema.References.Where(r =>
                         r.ReferencedCollection.Equals(parentCollectionName, StringComparison.OrdinalIgnoreCase)))
            {
                var parentKey = IndexValueExtractor.GetPropertyValue(parentDocument, reference.ReferencedField)?.ToString();
                if (string.IsNullOrEmpty(parentKey) && parentDocument is ILoahDocument loahDoc)
                {
                    parentKey = loahDoc.Id;
                }

                if (string.IsNullOrEmpty(parentKey))
                {
                    continue;
                }

                var childIds = child.FindIdsReferencing(reference.LocalField, parentKey);
                if (childIds.Count == 0)
                {
                    continue;
                }

                switch (reference.OnDelete)
                {
                    case LoahDeleteAction.Restrict:
                        throw new LoahSchemaException(
                            $"Cannot delete document from '{parentCollectionName}' because collection '{child.CollectionName}' still references it (field '{reference.LocalField}').");
                    case LoahDeleteAction.Cascade:
                        foreach (var childId in childIds.ToList())
                        {
                            child.DeleteById(childId);
                        }

                        break;
                    case LoahDeleteAction.SetNull:
                        foreach (var childId in childIds)
                        {
                            child.TrySetFieldNull(childId, reference.LocalField);
                        }

                        break;
                }
            }
        }
    }

    private void ValidateReferenceIntegrity(LoahIntegrityReport report)
    {
        foreach (var child in _referenceQueryables.Values)
        {
            var schema = child.Schema;
            if (schema is null)
            {
                continue;
            }

            foreach (var reference in schema.References)
            {
                if (!_referenceQueryables.TryGetValue(reference.ReferencedCollection, out var parent))
                {
                    continue;
                }

                foreach (var id in child.AllDocumentIds())
                {
                    if (!child.TryGetFieldValue(id, reference.LocalField, out var local) || string.IsNullOrEmpty(local))
                    {
                        continue;
                    }

                    if (!parent.ExistsFieldValue(reference.ReferencedField, local))
                    {
                        report.Errors.Add(
                            $"Reference violation: '{child.CollectionName}.{reference.LocalField}' = '{local}' was not found in '{reference.ReferencedCollection}'.");
                    }
                }
            }
        }
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
