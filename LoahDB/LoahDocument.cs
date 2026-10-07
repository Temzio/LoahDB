namespace LoahDB;

/// <summary>
/// Base document type with a generated identifier when none is supplied.
/// </summary>
public abstract class LoahDocument : ILoahDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
}
