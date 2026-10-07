using Newtonsoft.Json;

namespace LoahDB;

/// <summary>
/// Global configuration for a Loah database instance.
/// </summary>
public sealed class LoahOptions
{
    /// <summary>
    /// Base directory for all databases. Defaults to the OS application data folder.
    /// </summary>
    public string BasePath { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>
    /// Optional AES encryption key applied to every persisted file in this store.
    /// </summary>
    public string? EncryptionKey { get; set; }

    /// <summary>PBKDF2 iteration count when deriving keys from <see cref="EncryptionKey"/>.</summary>
    public int KeyDerivationIterations { get; set; } = 100_000;

    /// <summary>
    /// Newtonsoft.Json settings used for serialization.
    /// </summary>
    public JsonSerializerSettings SerializerSettings { get; set; } = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
    };

    /// <summary>
    /// How long to wait when acquiring an exclusive file lock.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When true, writes go to a temporary file and are atomically renamed into place.
    /// </summary>
    public bool AtomicWrites { get; set; } = true;

    /// <summary>
    /// Schema version stored in database metadata (see <see cref="LoahStore"/>).
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Storage engine layout. New stores default to <see cref="LoahStorageFormat.PageFile"/>.
    /// </summary>
    public LoahStorageFormat StorageFormat { get; set; } = LoahStorageFormat.PageFile;

    /// <summary>
    /// Maximum number of 4 KB pages kept in the LRU cache (page-file format only).
    /// </summary>
    public int PageCacheCapacity { get; set; } = 512;

    public LoahOptions Clone() => new()
    {
        BasePath = BasePath,
        EncryptionKey = EncryptionKey,
        KeyDerivationIterations = KeyDerivationIterations,
        SerializerSettings = SerializerSettings,
        LockTimeout = LockTimeout,
        AtomicWrites = AtomicWrites,
        SchemaVersion = SchemaVersion,
        StorageFormat = StorageFormat,
        PageCacheCapacity = PageCacheCapacity,
    };
}
