namespace LoahDB;

internal sealed class LoahIndexDefinition
{
    public string Name { get; set; } = "";
    public string PropertyName { get; set; } = "";
    public bool Unique { get; set; }
}
