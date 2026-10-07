namespace LoahDB;

public enum LoahChangeKind
{
    Insert,
    Update,
    Delete,
    BulkInsert,
}

public sealed class LoahCollectionChangedEventArgs : EventArgs
{
    public LoahCollectionChangedEventArgs(LoahChangeKind kind, int affectedCount)
    {
        Kind = kind;
        AffectedCount = affectedCount;
    }

    public LoahChangeKind Kind { get; }
    public int AffectedCount { get; }
}
