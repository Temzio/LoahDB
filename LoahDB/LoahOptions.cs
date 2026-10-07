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

    public LoahOptions Clone() => new()
    {
        BasePath = BasePath,
        EncryptionKey = EncryptionKey,
        SerializerSettings = SerializerSettings,
        LockTimeout = LockTimeout,
        AtomicWrites = AtomicWrites,
        SchemaVersion = SchemaVersion,
    };
}
