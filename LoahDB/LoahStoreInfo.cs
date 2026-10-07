namespace LoahDB;

public sealed class LoahStoreInfo
{
    public LoahStoreInfo(int schemaVersion, DateTime createdAtUtc, DateTime updatedAtUtc, IReadOnlyList<string> collections)
    {
        SchemaVersion = schemaVersion;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        Collections = collections;
    }

    public int SchemaVersion { get; }
    public DateTime CreatedAtUtc { get; }
    public DateTime UpdatedAtUtc { get; }
    public IReadOnlyList<string> Collections { get; }
}
