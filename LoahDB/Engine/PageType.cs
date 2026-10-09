namespace LoahDB.Engine;

internal enum PageType : byte
{
    Free = 0,
    Header = 1,
    BTreeLeaf = 2,
    BTreeInternal = 3,
    Overflow = 4,
}
