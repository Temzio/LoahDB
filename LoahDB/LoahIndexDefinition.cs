namespace LoahDB;

internal sealed class LoahIndexDefinition
{
    public string Name { get; set; } = "";
    public string PropertyName { get; set; } = "";
    public List<string> PropertyPaths { get; set; } = new();
    public bool Unique { get; set; }

    /// <summary>Root B+Tree page for this index (page-file stores only).</summary>
    public uint RootPageId { get; set; }

    public IReadOnlyList<string> GetPaths() =>
        PropertyPaths.Count > 0 ? PropertyPaths : new List<string> { PropertyName };
}
