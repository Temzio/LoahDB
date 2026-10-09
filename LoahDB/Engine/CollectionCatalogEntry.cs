namespace LoahDB.Engine;

internal sealed class CollectionCatalogEntry
{
    public uint RootPageId { get; set; }
    public List<LoahIndexDefinition> IndexDefinitions { get; set; } = new();
    public LoahCollectionSchema? Schema { get; set; }
    public List<LoahFullTextIndexDefinition> FullTextIndexDefinitions { get; set; } = new();
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
