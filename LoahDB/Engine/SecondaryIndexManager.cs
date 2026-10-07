using System.Text;

namespace LoahDB.Engine;

internal sealed class SecondaryIndexManager
{
    private readonly LoahPageDatabase _database;
    private readonly Dictionary<string, BPlusTree> _trees = new(StringComparer.OrdinalIgnoreCase);

    public SecondaryIndexManager(LoahPageDatabase database) => _database = database;

    public void Register(LoahIndexDefinition definition)
    {
        if (definition.RootPageId == 0)
        {
            var created = _database.CreateTree();
            definition.RootPageId = created.RootPageId;
        }

        _trees[definition.Name] = _database.OpenTree(definition.RootPageId);
    }

    public void Rebuild(LoahIndexDefinition definition, IEnumerable<(string DocumentId, object Document)> documents)
    {
        var tree = _database.CreateTree();
        definition.RootPageId = tree.RootPageId;
        _trees[definition.Name] = tree;

        foreach (var (docId, doc) in documents)
        {
            AddDocumentToTree(definition, tree, docId, doc);
        }
    }

    public void AddDocument(LoahIndexDefinition definition, string documentId, object document)
    {
        var tree = GetTree(definition);
        AddDocumentToTree(definition, tree, documentId, document);
    }

    public void RemoveDocument(LoahIndexDefinition definition, string documentId, object document)
    {
        var tree = GetTree(definition);
        foreach (var sortKey in GetSortKeys(definition, document))
        {
            var key = definition.Unique
                ? sortKey
                : IndexKeyEncoding.MakeUniqueTreeKey(sortKey, documentId);
            tree.Delete(key);
        }
    }

    public string? FindFirstDocumentId(LoahIndexDefinition definition, string encodedLookupKey)
    {
        var tree = GetTree(definition);
        if (definition.Unique)
        {
            return tree.TryGet(encodedLookupKey, out var bytes)
                ? IndexKeyEncoding.FromBytes(bytes)
                : null;
        }

        foreach (var (key, _) in tree.Scan())
        {
            if (IndexKeyEncoding.ExtractSortKey(key, false) == encodedLookupKey)
            {
                var separator = key.LastIndexOf('\0');
                return separator < 0 ? null : key[(separator + 1)..];
            }
        }

        return null;
    }

    public IEnumerable<string> RangeDocumentIds(
        LoahIndexDefinition definition,
        string? minInclusive,
        string? maxInclusive,
        bool descending)
    {
        var tree = GetTree(definition);
        var entries = tree.Scan()
            .Select(pair =>
            {
                var sortKey = IndexKeyEncoding.ExtractSortKey(pair.Key, definition.Unique);
                var docId = definition.Unique
                    ? IndexKeyEncoding.FromBytes(pair.Value)
                    : pair.Key[(pair.Key.LastIndexOf('\0') + 1)..];
                return (sortKey, docId);
            })
            .Where(e => IsInRange(e.sortKey, minInclusive, maxInclusive))
            .GroupBy(e => e.docId, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(e => e.sortKey, descending
                ? Comparer<string>.Create((a, b) => string.CompareOrdinal(b, a))
                : Comparer<string>.Default)
            .Select(e => e.docId);

        return entries;
    }

    public bool HasUniqueKey(LoahIndexDefinition definition, string encodedKey, string? excludeDocumentId)
    {
        if (!definition.Unique)
        {
            return false;
        }

        var existing = FindFirstDocumentId(definition, encodedKey);
        return existing is not null &&
               !existing.Equals(excludeDocumentId, StringComparison.Ordinal);
    }

    private BPlusTree GetTree(LoahIndexDefinition definition)
    {
        if (_trees.TryGetValue(definition.Name, out var tree))
        {
            return tree;
        }

        Register(definition);
        return _trees[definition.Name];
    }

    private static void AddDocumentToTree(
        LoahIndexDefinition definition,
        BPlusTree tree,
        string documentId,
        object document)
    {
        foreach (var sortKey in GetSortKeys(definition, document))
        {
            var key = definition.Unique
                ? sortKey
                : IndexKeyEncoding.MakeUniqueTreeKey(sortKey, documentId);
            var value = definition.Unique
                ? IndexKeyEncoding.ToBytes(documentId)
                : Array.Empty<byte>();
            tree.Insert(key, value);
        }
    }

    private static IEnumerable<string> GetSortKeys(LoahIndexDefinition definition, object document)
    {
        var paths = definition.PropertyPaths.Count > 0
            ? definition.PropertyPaths
            : new List<string> { definition.PropertyName };

        foreach (var raw in IndexValueExtractor.GetKeyValues(document, paths))
        {
            if (raw is IList<object?> list)
            {
                yield return IndexKeyEncoding.EncodeComposite(list);
            }
            else
            {
                yield return IndexKeyEncoding.EncodeSingle(raw);
            }
        }
    }

    private static bool IsInRange(string sortKey, string? minInclusive, string? maxInclusive)
    {
        if (minInclusive is not null && string.CompareOrdinal(sortKey, minInclusive) < 0)
        {
            return false;
        }

        if (maxInclusive is not null && string.CompareOrdinal(sortKey, maxInclusive) > 0)
        {
            return false;
        }

        return true;
    }
}
