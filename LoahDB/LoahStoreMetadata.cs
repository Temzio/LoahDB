namespace LoahDB;

internal sealed class LoahStoreMetadata
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<string> Collections { get; set; } = new();
}
