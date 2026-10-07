namespace LoahDB;

/// <summary>
/// Foreign-key-like reference from a field in this collection to another collection.
/// </summary>
public sealed class LoahReferenceDefinition
{
    public string LocalField { get; set; } = "";
    public string ReferencedCollection { get; set; } = "";
    public string ReferencedField { get; set; } = "Id";
    public LoahDeleteAction OnDelete { get; set; } = LoahDeleteAction.Restrict;
}
