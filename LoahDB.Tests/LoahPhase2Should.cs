using LoahDB.Engine;

namespace LoahDB.Tests;

public class LoahPhase2Should : IDisposable
{
    private readonly string _root = "Phase2_" + Guid.NewGuid().ToString("N");
    private readonly LoahOptions _pageOptions;
    private readonly string _dbPath;

    public LoahPhase2Should()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "LoahDBTests");
        _pageOptions = new LoahOptions
        {
            BasePath = basePath,
            StorageFormat = LoahStorageFormat.PageFile,
            LockTimeout = TimeSpan.FromSeconds(2),
        };
        _dbPath = Path.Combine(basePath, _root + ".loahdb");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        if (File.Exists(_dbPath + "-wal"))
        {
            File.Delete(_dbPath + "-wal");
        }

        var lockFile = _dbPath + ".writer.lock";
        if (File.Exists(lockFile))
        {
            File.Delete(lockFile);
        }
    }

    [Fact]
    public void NestedBeginTransactionThrows()
    {
        using var store = new LoahStore(_root, _pageOptions);
        var tx = store.BeginTransaction();
        Assert.Throws<InvalidOperationException>(() => store.BeginTransaction());
        tx.Rollback();
    }

    [Fact]
    public void PageTransactionCommitsAcrossCollections()
    {
        using var store = new LoahStore(_root, _pageOptions);
        var a = store.Collection<Item>("a");
        var b = store.Collection<Item>("b");

        using (var tx = store.BeginTransaction())
        {
            a.Insert(new Item { Name = "A" });
            b.Insert(new Item { Name = "B" });
            tx.Commit();
        }

        using var reopened = new LoahStore(_root, _pageOptions);
        Assert.Equal(1, reopened.Collection<Item>("a").Count);
        Assert.Equal(1, reopened.Collection<Item>("b").Count);
    }

    [Fact]
    public void PageTransactionRollbackDiscardsAllCollections()
    {
        using var store = new LoahStore(_root, _pageOptions);
        store.Collection<Item>("a").Insert(new Item { Name = "Keep" });

        using (var tx = store.BeginTransaction())
        {
            store.Collection<Item>("a").Insert(new Item { Name = "Drop" });
            store.Collection<Item>("b").Insert(new Item { Name = "DropB" });
            tx.Rollback();
        }

        Assert.Equal(1, store.Collection<Item>("a").Count);
        Assert.Equal(0, store.Collection<Item>("b").Count);
    }

    [Fact]
    public void WalFlushFailureRollsBackInMemoryState()
    {
        var walPath = _dbPath + "-wal";
        using (var db = new LoahPageDatabase(_dbPath, 64, new FaultInjectingFileStream(
                   new FileStream(walPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read),
                   failAfterWrites: 0)))
        {
            db.BeginWriteTransaction(TimeSpan.FromSeconds(5));
            db.OpenCatalog().Insert("pending", System.Text.Encoding.UTF8.GetBytes("{\"x\":1}"));
            Assert.Throws<IOException>(() => db.CommitWriteTransaction());
        }

        using var reopened = new LoahPageDatabase(_dbPath, 64);
        Assert.False(reopened.OpenCatalog().TryGet("pending", out _));
    }

    [Fact]
    public void OpenReplaysCommittedWalOntoMainFile()
    {
        using (var db = new LoahPageDatabase(_dbPath, 64))
        {
            db.CreateTree();
        }

        var header = new byte[LoahConstants.PageSize];
        using (var fs = new FileStream(_dbPath, FileMode.Open, FileAccess.Read))
        {
            fs.ReadExactly(header, 0, header.Length);
        }

        var marker = new byte[LoahConstants.PageSize];
        marker[0] = (byte)PageType.Overflow;
        using (var wal = new WriteAheadLog(_dbPath + "-wal"))
        {
            wal.AppendPage(9, marker);
            wal.AppendHeader(header);
            wal.AppendCommit();
            wal.FlushToDisk();
        }

        using var recovered = new LoahPageDatabase(_dbPath, 64);
        var page = recovered.GetPage(9);
        Assert.Equal(PageType.Overflow, page.Type);
    }

    private sealed class Item : LoahDocument
    {
        public string Name { get; set; } = "";
    }
}
