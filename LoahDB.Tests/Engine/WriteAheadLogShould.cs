using LoahDB.Engine;

namespace LoahDB.Tests.Engine;

public class WriteAheadLogShould : IDisposable
{
    private readonly string _walPath = Path.Combine(Path.GetTempPath(), "loah-wal-" + Guid.NewGuid().ToString("N") + "-wal");

    public void Dispose()
    {
        if (File.Exists(_walPath))
        {
            File.Delete(_walPath);
        }
    }

    [Fact]
    public void ReplayReturnsLastCommittedTransaction()
    {
        var page = new byte[LoahConstants.PageSize];
        page[0] = (byte)PageType.BTreeLeaf;
        var header = new byte[LoahConstants.PageSize];
        LoahConstants.MagicBytes.CopyTo(header);

        using (var wal = new WriteAheadLog(_walPath))
        {
            wal.AppendPage(3, page);
            wal.AppendHeader(header);
            wal.AppendCommit();
            wal.FlushToDisk();
        }

        using var replayWal = new WriteAheadLog(_walPath);
        var result = replayWal.ReplayCommitted();
        Assert.True(result.HasCommittedTransaction);
        Assert.True(result.Pages.ContainsKey(3));
        Assert.NotNull(result.HeaderPage);
    }

    [Fact]
    public void UncommittedFramesAreNotReplayed()
    {
        var page = new byte[LoahConstants.PageSize];
        using (var wal = new WriteAheadLog(_walPath))
        {
            wal.AppendPage(1, page);
            wal.FlushToDisk();
        }

        using var replayWal = new WriteAheadLog(_walPath);
        var result = replayWal.ReplayCommitted();
        Assert.False(result.HasCommittedTransaction);
    }
}
