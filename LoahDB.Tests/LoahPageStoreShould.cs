namespace LoahDB.Tests;

public class LoahPageStoreShould : IDisposable
{
    private readonly string _root = "PageStore_" + Guid.NewGuid().ToString("N");
    private readonly LoahOptions _options;

    public LoahPageStoreShould()
    {
        _options = new LoahOptions
        {
            BasePath = Path.Combine(Path.GetTempPath(), "LoahDBTests"),
            StorageFormat = LoahStorageFormat.PageFile,
            PageCacheCapacity = 64,
        };
    }

    public void Dispose()
    {
        var dbFile = Path.Combine(_options.BasePath, _root + ".loahdb");
        if (File.Exists(dbFile))
        {
            File.Delete(dbFile);
        }

        var path = Path.Combine(_options.BasePath, _root);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void InsertsSurviveReopen()
    {
        using (var store = new LoahStore(_root, _options))
        {
            var docs = store.Collection<Item>("items");
            docs.Insert(new Item { Name = "alpha", Value = 1 });
            docs.Insert(new Item { Name = "beta", Value = 2 });
        }

        using var reopened = new LoahStore(_root, _options);
        var again = reopened.Collection<Item>("items");
        Assert.Equal(2, again.Count);
        Assert.Contains(again.All(), i => i.Name == "beta");
    }

    [Fact]
    public void ImportLegacyV1CopiesJsonCollections()
    {
        var jsonOptions = new LoahOptions
        {
            BasePath = _options.BasePath,
            StorageFormat = LoahStorageFormat.JsonCollections,
        };
        var jsonStore = new LoahStore(_root, jsonOptions);
        jsonStore.Collection<Item>("items").Insert(new Item { Name = "imported", Value = 42 });

        using var pageStore = new LoahStore(_root, _options);
        pageStore.ImportLegacyV1();

        var items = pageStore.Collection<Item>("items");
        Assert.Equal(1, items.Count);
        Assert.Equal("imported", items.All()[0].Name);
    }

    private sealed class Item : LoahDocument
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }
}
