using Newtonsoft.Json;

namespace LoahDB;

/// <summary>
/// Thread-safe, optionally encrypted file persistence with atomic replace.
/// </summary>
public sealed class LoahStorage
{
    private readonly LoahOptions _options;

    /// <summary>Increments on each successful <see cref="Write{T}"/> (for tests).</summary>
    internal int WriteInvocationCount { get; private set; }

    public LoahStorage(LoahOptions options)
    {
        _options = options.Clone();
    }

    public string ResolvePath(string root, string key, string extension = ".loah")
    {
        var folder = Path.Combine(_options.BasePath, root);
        return Path.Combine(folder, key + extension);
    }

    public string ResolveCollectionPath(string storeRoot, string collectionName) =>
        ResolvePath(Path.Combine(storeRoot, "_collections"), collectionName, ".loah");

    public bool Exists(string filePath) => File.Exists(filePath);

    public T? Read<T>(string filePath)
    {
        using (AcquireLock(filePath))
        {
            if (!File.Exists(filePath))
            {
                return default;
            }

            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return default;
            }

            if (!string.IsNullOrEmpty(_options.EncryptionKey))
            {
                json = CryptoLoah.Decrypt(json, _options.EncryptionKey);
            }

            return JsonConvert.DeserializeObject<T>(json, _options.SerializerSettings);
        }
    }

    public async Task<T?> ReadAsync<T>(string filePath, CancellationToken cancellationToken = default)
    {
        using (AcquireLock(filePath))
        {
            if (!File.Exists(filePath))
            {
                return default;
            }

            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return default;
            }

            if (!string.IsNullOrEmpty(_options.EncryptionKey))
            {
                json = CryptoLoah.Decrypt(json, _options.EncryptionKey);
            }

            return JsonConvert.DeserializeObject<T>(json, _options.SerializerSettings);
        }
    }

    public void Write<T>(string filePath, T data)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonConvert.SerializeObject(data, _options.SerializerSettings);
        if (!string.IsNullOrEmpty(_options.EncryptionKey))
        {
            json = CryptoLoah.Encrypt(json, _options.EncryptionKey);
        }

        using (AcquireLock(filePath))
        {
            if (_options.AtomicWrites)
            {
                var tempPath = filePath + ".tmp." + Guid.NewGuid().ToString("N");
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, filePath, overwrite: true);
            }
            else
            {
                File.WriteAllText(filePath, json);
            }

            WriteInvocationCount++;
        }
    }

    public async Task WriteAsync<T>(string filePath, T data, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonConvert.SerializeObject(data, _options.SerializerSettings);
        if (!string.IsNullOrEmpty(_options.EncryptionKey))
        {
            json = CryptoLoah.Encrypt(json, _options.EncryptionKey);
        }

        using (AcquireLock(filePath))
        {
            if (_options.AtomicWrites)
            {
                var tempPath = filePath + ".tmp." + Guid.NewGuid().ToString("N");
                await File.WriteAllTextAsync(tempPath, json, cancellationToken);
                File.Move(tempPath, filePath, overwrite: true);
            }
            else
            {
                await File.WriteAllTextAsync(filePath, json, cancellationToken);
            }
        }
    }

    public void Delete(string filePath)
    {
        using (AcquireLock(filePath))
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    public IReadOnlyList<string> ListKeys(string root, string extension = ".loah")
    {
        var directory = Path.Combine(_options.BasePath, root);
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return Directory
            .GetFiles(directory, "*" + extension)
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    private LoahFileLock AcquireLock(string filePath) =>
        LoahFileLock.Acquire(filePath, _options.LockTimeout);
}
