namespace LoahDB;

/// <summary>Full-text index on a single string property path.</summary>
public sealed class LoahFullTextIndexDefinition
{
    public string Name { get; set; } = "";
    public string PropertyPath { get; set; } = "";
}
