namespace LoahDB;

/// <summary>
/// A document stored in a <see cref="LoahCollection{T}"/>.
/// </summary>
public interface ILoahDocument
{
    /// <summary>
    /// Unique identifier within the collection.
    /// </summary>
    string Id { get; set; }
}
