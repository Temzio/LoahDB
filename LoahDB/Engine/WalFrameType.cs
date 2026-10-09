namespace LoahDB.Engine;

internal enum WalFrameType : uint
{
    Page = 1,
    Header = 2,
    Commit = 3,
    Checkpoint = 4,
}
