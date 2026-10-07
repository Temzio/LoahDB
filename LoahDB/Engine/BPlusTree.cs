using System.Text;

namespace LoahDB.Engine;

internal sealed class BPlusTree
{
    private readonly LoahPageDatabase _db;
    private uint _rootPageId;

    public BPlusTree(LoahPageDatabase db, uint rootPageId)
    {
        _db = db;
        _rootPageId = rootPageId;
    }

    public uint RootPageId => _rootPageId;

    public void SetRoot(uint rootPageId) => _rootPageId = rootPageId;

    public bool TryGet(string key, out byte[] value)
    {
        if (!TryFindLeaf(key, out var leaf, out _))
        {
            value = Array.Empty<byte>();
            return false;
        }

        foreach (var (k, v) in BTreeRecords.ReadAll(leaf))
        {
            if (k == key)
            {
                value = _db.ReadValue(v);
                return true;
            }
        }

        value = Array.Empty<byte>();
        return false;
    }

    public void Insert(string key, byte[] value)
    {
        var stored = _db.StoreValue(value);
        var split = InsertInto(_rootPageId, key, stored);
        if (split is not null)
        {
            var newRoot = _db.AllocatePage();
            BTreeRecords.TryWriteInternal(_db.GetPage(newRoot), new List<(string, uint)>
            {
                ("", split.LeftPageId),
                (split.SeparatorKey, split.RightPageId),
            });
            _db.MarkDirty(newRoot);
            _rootPageId = newRoot;
        }

        _db.Flush();
    }

    public bool Delete(string key)
    {
        if (!TryFindLeaf(key, out var leaf, out var leafPageId))
        {
            return false;
        }

        var entries = BTreeRecords.ReadAll(leaf);
        var idx = entries.FindIndex(e => e.Key == key);
        if (idx < 0)
        {
            return false;
        }

        entries.RemoveAt(idx);
        BTreeRecords.TryWriteLeaf(leaf, entries);
        _db.MarkDirty(leafPageId);
        _db.Flush();
        return true;
    }

    public IEnumerable<(string Key, byte[] Value)> Scan()
    {
        foreach (var key in ScanKeys())
        {
            if (TryGet(key, out var value))
            {
                yield return (key, value);
            }
        }
    }

    private IEnumerable<string> ScanKeys()
    {
        var leaves = new List<uint>();
        CollectLeaves(_rootPageId, leaves);
        foreach (var leafId in leaves.Order())
        {
            foreach (var (k, _) in BTreeRecords.ReadAll(_db.GetPage(leafId)).OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                yield return k;
            }
        }
    }

    private void CollectLeaves(uint pageId, List<uint> leaves)
    {
        var page = _db.GetPage(pageId);
        if (page.Type == PageType.BTreeLeaf)
        {
            leaves.Add(pageId);
            return;
        }

        foreach (var (_, child) in BTreeRecords.ReadInternal(page))
        {
            CollectLeaves(child, leaves);
        }
    }

    private SplitResult? InsertInto(uint pageId, string key, byte[] stored)
    {
        var page = _db.GetPage(pageId);
        if (page.Type == PageType.BTreeLeaf)
        {
            var entries = BTreeRecords.ReadAll(page);
            var idx = entries.FindIndex(e => e.Key == key);
            if (idx >= 0)
            {
                entries[idx] = (key, stored);
            }
            else
            {
                entries.Add((key, stored));
            }

            entries.Sort((a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
            if (BTreeRecords.TryWriteLeaf(page, entries))
            {
                _db.MarkDirty(pageId);
                return null;
            }

            return SplitLeaf(pageId, entries);
        }

        var internalEntries = BTreeRecords.ReadInternal(page);
        var childIndex = 0;
        for (var i = 1; i < internalEntries.Count; i++)
        {
            if (string.Compare(key, internalEntries[i].Key, StringComparison.Ordinal) >= 0)
            {
                childIndex = i;
            }
        }

        var childPage = internalEntries[childIndex].Child;
        var childSplit = InsertInto(childPage, key, stored);
        if (childSplit is null)
        {
            return null;
        }

        internalEntries.Insert(childIndex + 1, (childSplit.SeparatorKey, childSplit.RightPageId));
        if (BTreeRecords.TryWriteInternal(page, internalEntries))
        {
            _db.MarkDirty(pageId);
            return null;
        }

        return SplitInternal(pageId, internalEntries);
    }

    private SplitResult SplitLeaf(uint leftPageId, List<(string Key, byte[] Value)> entries)
    {
        var mid = entries.Count / 2;
        var left = entries.Take(mid).ToList();
        var right = entries.Skip(mid).ToList();
        var rightPageId = _db.AllocatePage();
        BTreeRecords.TryWriteLeaf(_db.GetPage(leftPageId), left);
        BTreeRecords.TryWriteLeaf(_db.GetPage(rightPageId), right);
        _db.MarkDirty(leftPageId);
        _db.MarkDirty(rightPageId);
        return new SplitResult(leftPageId, right[0].Key, rightPageId);
    }

    private SplitResult SplitInternal(uint leftPageId, List<(string Key, uint Child)> entries)
    {
        var mid = entries.Count / 2;
        var promoteKey = entries[mid].Key;
        var left = entries.Take(mid).ToList();
        var right = entries.Skip(mid + 1).ToList();
        var rightPageId = _db.AllocatePage();
        BTreeRecords.TryWriteInternal(_db.GetPage(leftPageId), left);
        BTreeRecords.TryWriteInternal(_db.GetPage(rightPageId), right);
        _db.MarkDirty(leftPageId);
        _db.MarkDirty(rightPageId);
        return new SplitResult(leftPageId, promoteKey, rightPageId);
    }

    private bool TryFindLeaf(string key, out PageBuffer leaf, out uint leafPageId)
    {
        var pageId = _rootPageId;
        while (true)
        {
            var page = _db.GetPage(pageId);
            if (page.Type == PageType.BTreeLeaf)
            {
                leaf = page;
                leafPageId = pageId;
                return true;
            }

            if (page.Type != PageType.BTreeInternal)
            {
                leaf = page;
                leafPageId = pageId;
                return false;
            }

            var entries = BTreeRecords.ReadInternal(page);
            var child = entries[0].Child;
            for (var i = 1; i < entries.Count; i++)
            {
                if (string.Compare(key, entries[i].Key, StringComparison.Ordinal) < 0)
                {
                    break;
                }

                child = entries[i].Child;
            }

            pageId = child;
        }
    }

    private sealed record SplitResult(uint LeftPageId, string SeparatorKey, uint RightPageId);
}
