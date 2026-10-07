namespace LoahDB;

internal sealed class LoahFileLock : IDisposable
{
    private readonly FileStream _stream;
    private bool _disposed;

    private LoahFileLock(FileStream stream) => _stream = stream;

    public static LoahFileLock Acquire(string filePath, TimeSpan timeout)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var lockPath = filePath + ".lock";
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                return new LoahFileLock(stream);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(15);
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
