namespace LoahDB;

/// <summary>
/// Stages collection writes and commits them atomically per file.
/// </summary>
public sealed class LoahTransaction : IDisposable
{
    private readonly LoahStore _store;
    private readonly Dictionary<string, object> _pending = new();
    private bool _committed;
    private bool _disposed;

    internal LoahTransaction(LoahStore store) => _store = store;

    internal void StageWrite(string filePath, object payload) => _pending[filePath] = payload;

    public void Commit()
    {
        if (_committed)
        {
            return;
        }

        foreach (var (path, payload) in _pending)
        {
            _store.Storage.Write(path, payload);
        }

        _pending.Clear();
        _committed = true;
        _store.EndTransaction(this);
    }

    public void Rollback()
    {
        _pending.Clear();
        _committed = true;
        _store.EndTransaction(this);
        foreach (var collection in _store.GetLoadedCollections())
        {
            collection.Reload();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_committed)
        {
            Rollback();
        }
    }
}
