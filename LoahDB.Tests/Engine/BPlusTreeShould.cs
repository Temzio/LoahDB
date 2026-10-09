using LoahDB.Engine;
using LoahConstants = LoahDB.Engine.LoahConstants;

namespace LoahDB.Tests.Engine;

public class BPlusTreeShould : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "loah-btree-" + Guid.NewGuid().ToString("N") + ".loahdb");

    public void Dispose()
    {
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }

    [Fact]
    public void HeaderSurvivesReopen()
    {
        using (var db = new LoahPageDatabase(_file, 128))
        {
            db.CreateTree();
            db.Flush();
            var onDisk = File.ReadAllBytes(_file);
            Assert.True(onDisk.Length >= LoahConstants.PageSize);
            Assert.True(onDisk.AsSpan(0, 7).SequenceEqual(LoahConstants.MagicBytes));
        }

        var headerBytes = File.ReadAllBytes(_file).AsSpan(0, 7);
        Assert.True(headerBytes.SequenceEqual(LoahConstants.MagicBytes));

        using var reopened = new LoahPageDatabase(_file, 128);
        Assert.True(File.Exists(_file));
        Assert.True(new FileInfo(_file).Length >= LoahConstants.PageSize);
    }

    [Fact]
    public void InsertsManyKeysAndFindsThem()
    {
        using var db = new LoahPageDatabase(_file, 128);
        var tree = db.CreateTree();
        for (var i = 0; i < 5_000; i++)
        {
            var key = i.ToString("D8");
            tree.Insert(key, System.Text.Encoding.UTF8.GetBytes("value-" + i));
        }

        Assert.True(tree.TryGet("00004200", out var value));
        Assert.Equal("value-4200", System.Text.Encoding.UTF8.GetString(value));
        Assert.Equal(5_000, tree.Scan().Count());
    }
}
