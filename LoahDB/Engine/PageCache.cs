namespace LoahDB.Engine;

internal sealed class PageCache
{
    private readonly int _capacity;
    private readonly Action<PageBuffer>? _flushOnEvict;
    private readonly Func<PageBuffer, bool>? _canEvict;
    private readonly Dictionary<uint, LinkedListNode<CacheEntry>> _map = new();
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly object _lock = new();

    public PageCache(int capacity, Action<PageBuffer>? flushOnEvict = null, Func<PageBuffer, bool>? canEvict = null)
    {
        _capacity = Math.Max(16, capacity);
        _flushOnEvict = flushOnEvict;
        _canEvict = canEvict;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _lru.Clear();
        }
    }

    public PageBuffer GetOrAdd(uint pageId, Func<uint, PageBuffer> loader)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(pageId, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Page;
            }

            var page = loader(pageId);
            var entry = new CacheEntry(pageId, page);
            var newNode = new LinkedListNode<CacheEntry>(entry);
            _lru.AddFirst(newNode);
            _map[pageId] = newNode;
            EvictIfNeeded();
            return page;
        }
    }

    public void MarkDirty(uint pageId)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(pageId, out var node))
            {
                node.Value.Page.IsDirty = true;
            }
        }
    }

    public IEnumerable<PageBuffer> GetDirtyPages()
    {
        lock (_lock)
        {
            foreach (var node in _lru)
            {
                if (node.Page.IsDirty)
                {
                    yield return node.Page;
                }
            }
        }
    }

    public void ClearDirty(uint pageId)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(pageId, out var node))
            {
                node.Value.Page.IsDirty = false;
            }
        }
    }

    private void EvictIfNeeded()
    {
        while (_map.Count > _capacity)
        {
            var last = _lru.Last;
            if (last is null)
            {
                break;
            }

            var page = last.Value.Page;
            if (_canEvict is not null && !_canEvict(page))
            {
                break;
            }

            if (page.IsDirty)
            {
                _flushOnEvict?.Invoke(page);
                page.IsDirty = false;
            }

            _lru.RemoveLast();
            _map.Remove(last.Value.PageId);
        }
    }

    private sealed class CacheEntry
    {
        public CacheEntry(uint pageId, PageBuffer page)
        {
            PageId = pageId;
            Page = page;
        }

        public uint PageId { get; }
        public PageBuffer Page { get; }
    }
}
