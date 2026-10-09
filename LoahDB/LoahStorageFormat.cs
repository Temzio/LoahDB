namespace LoahDB;

/// <summary>
/// On-disk layout for a <see cref="LoahStore"/>.
/// </summary>
public enum LoahStorageFormat
{
    /// <summary>One JSON file per collection under <c>{root}/_collections/</c>.</summary>
    JsonCollections = 0,

    /// <summary>Single page-based <c>{root}.loahdb</c> file.</summary>
    PageFile = 1,
}
