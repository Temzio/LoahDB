namespace LoahDB.Tests;

public class LoahPhase3Should : IDisposable
{
    private readonly string _root = "Phase3_" + Guid.NewGuid().ToString("N");
    private readonly LoahOptions _pageOptions;

    public LoahPhase3Should()
    {
        _pageOptions = new LoahOptions
        {
            BasePath = Path.Combine(Path.GetTempPath(), "LoahDBTests"),
            StorageFormat = LoahStorageFormat.PageFile,
        };
    }

    public void Dispose()
    {
        var db = Path.Combine(_pageOptions.BasePath, _root + ".loahdb");
        if (File.Exists(db))
        {
            File.Delete(db);
        }

        if (File.Exists(db + "-wal"))
        {
            File.Delete(db + "-wal");
        }
    }

    [Fact]
    public void CompositeUniqueIndexFindsDocument()
    {
        using var store = new LoahStore(_root, _pageOptions);
        var col = store.Collection<Person>("people");
        col.EnsureCompositeIndex("by_name_age", p => p.Name, p => p.Age, unique: true);
        var doc = col.Insert(new Person { Name = "Ada", Age = 36 });

        var found = col.FindByIndexParts("by_name_age", "Ada", 36);
        Assert.NotNull(found);
        Assert.Equal(doc.Id, found!.Id);
    }

    [Fact]
    public void NestedPropertyIndexSupportsRangeScanDescending()
    {
        using var store = new LoahStore(_root, _pageOptions);
        var col = store.Collection<Person>("people");
        col.EnsureIndex("by_city", p => p.Address.City);
        col.Insert(new Person { Name = "A", Address = new Address { City = "Boston" } });
        col.Insert(new Person { Name = "B", Address = new Address { City = "Chicago" } });
        col.Insert(new Person { Name = "C", Address = new Address { City = "Denver" } });

        var desc = col.FindByIndexRange("by_city", "Boston", "Denver", descending: true);
        Assert.Equal(3, desc.Count);
        Assert.Equal("Denver", desc[0].Address.City);
        Assert.Equal("Chicago", desc[1].Address.City);

        var mid = col.FindByIndexRange("by_city", "Chicago", "Chicago");
        Assert.Single(mid);
        Assert.Equal("Chicago", mid[0].Address.City);
    }

    [Fact]
    public void IndexUpdatesRollBackWithTransaction()
    {
        using var store = new LoahStore(_root, _pageOptions);
        var col = store.Collection<Person>("people");
        col.EnsureIndex("by_name", p => p.Name, unique: true);
        col.Insert(new Person { Name = "Keep" });

        using (var tx = store.BeginTransaction())
        {
            col.Insert(new Person { Name = "Drop" });
            tx.Rollback();
        }

        Assert.Null(col.FindByIndex("by_name", "Drop"));
        Assert.NotNull(col.FindByIndex("by_name", "Keep"));
    }

    private sealed class Person : LoahDocument
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public Address Address { get; set; } = new();
    }

    private sealed class Address
    {
        public string City { get; set; } = "";
    }
}
