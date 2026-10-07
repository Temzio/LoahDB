namespace LoahDB;

/// <summary>
/// Stages collection writes and commits them atomically across collections.
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

        var tempFiles = new List<(string TargetPath, string TempPath)>();
        try
        {
            foreach (var (path, payload) in _pending)
            {
                var tempPath = path + ".txtmp." + Guid.NewGuid().ToString("N");
                _store.Storage.Write(tempPath, payload);
                tempFiles.Add((path, tempPath));
            }

            _store.PageStore?.CommitTransaction();

            foreach (var (target, temp) in tempFiles)
            {
                File.Move(temp, target, overwrite: true);
            }

            _pending.Clear();
            _committed = true;
            _store.EndTransaction(this);
        }
        catch
        {
            foreach (var (_, temp) in tempFiles)
            {
                try
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            _store.PageStore?.RollbackTransaction();
            throw;
        }
    }

    public void Rollback()
    {
        _pending.Clear();
        _committed = true;
        _store.PageStore?.RollbackTransaction();
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
