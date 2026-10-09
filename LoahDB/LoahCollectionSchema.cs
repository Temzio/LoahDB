namespace LoahDB;

/// <summary>
/// Optional validation rules for documents in a collection.
/// </summary>
public sealed class LoahCollectionSchema
{
    public List<string> RequiredFields { get; set; } = new();
    public Dictionary<string, LoahSchemaType> FieldTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<LoahReferenceDefinition> References { get; set; } = new();
}
