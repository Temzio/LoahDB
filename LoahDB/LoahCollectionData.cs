namespace LoahDB;

internal sealed class LoahCollectionData<T>
{
    public string Name { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<T> Documents { get; set; } = new();
    public List<LoahIndexDefinition> IndexDefinitions { get; set; } = new();
    public Dictionary<string, Dictionary<string, List<string>>> Indexes { get; set; } = new();
}
