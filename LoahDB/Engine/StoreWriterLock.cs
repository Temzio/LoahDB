namespace LoahDB.Engine;

internal sealed class StoreWriterLock : IDisposable
{
    private readonly FileStream _stream;
    private bool _disposed;

    private StoreWriterLock(FileStream stream) => _stream = stream;

    public static StoreWriterLock Acquire(string databasePath, TimeSpan timeout)
    {
        var lockPath = databasePath + ".writer.lock";
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new StoreWriterLock(stream);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(15);
            }
            catch (IOException ex)
            {
                throw new LoahConcurrencyException(
                    $"Could not acquire writer lock for '{databasePath}' within {timeout.TotalSeconds:0.#}s.",
                    ex);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Dispose();
        try
        {
            File.Delete(_stream.Name);
        }
        catch
        {
            // Best-effort lock file cleanup.
        }
    }
}
