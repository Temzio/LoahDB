namespace LoahDB.Tests;

public class LoahPhase5Should : IDisposable
{
    private readonly string _root = "Phase5_" + Guid.NewGuid().ToString("N");
    private readonly LoahOptions _options;

    public LoahPhase5Should()
    {
        _options = new LoahOptions
        {
            BasePath = Path.Combine(Path.GetTempPath(), "LoahDBTests"),
            StorageFormat = LoahStorageFormat.PageFile,
        };
    }

    public void Dispose()
    {
        var db = Path.Combine(_options.BasePath, _root + ".loahdb");
        if (File.Exists(db))
        {
            File.Delete(db);
        }

        var wal = db + "-wal";
        if (File.Exists(wal))
        {
            File.Delete(wal);
        }
    }

    [Fact]
    public void RequiredFieldIsEnforcedOnInsert()
    {
        using var store = new LoahStore(_root, _options);
        var users = store.Collection<Person>("people");
        users.SetSchema(new LoahCollectionSchema
        {
            RequiredFields = { nameof(Person.Name) },
        });

        Assert.Throws<LoahSchemaException>(() => users.Insert(new Person { Email = "x@example.com" }));
        users.Insert(new Person { Name = "Ada", Email = "ada@example.com" });
        Assert.Equal(1, users.Count);
    }

    [Fact]
    public void ReferenceMustExistOnInsert()
    {
        using var store = new LoahStore(_root, _options);
        var users = store.Collection<Person>("users");
        var orders = store.Collection<OrderRow>("orders");
        orders.SetSchema(new LoahCollectionSchema
        {
            References =
            {
                new LoahReferenceDefinition
                {
                    LocalField = nameof(OrderRow.UserId),
                    ReferencedCollection = "users",
                    ReferencedField = nameof(ILoahDocument.Id),
                },
            },
        });

        users.Insert(new Person { Name = "Ada", Email = "ada@example.com" });
        Assert.Throws<LoahSchemaException>(() => orders.Insert(new OrderRow { UserId = "missing" }));
        orders.Insert(new OrderRow { UserId = users.GetById(users.All()[0].Id)!.Id });
        Assert.Equal(1, orders.Count);
    }

    [Fact]
    public void RestrictBlocksParentDelete()
    {
        using var store = new LoahStore(_root, _options);
        var users = store.Collection<Person>("users");
        var orders = store.Collection<OrderRow>("orders");
        orders.SetSchema(new LoahCollectionSchema
        {
            References =
            {
                new LoahReferenceDefinition
                {
                    LocalField = nameof(OrderRow.UserId),
                    ReferencedCollection = "users",
                    OnDelete = LoahDeleteAction.Restrict,
                },
            },
        });

        var user = users.Insert(new Person { Name = "Ada", Email = "ada@example.com" });
        orders.Insert(new OrderRow { UserId = user.Id });
        Assert.Throws<LoahSchemaException>(() => users.Delete(user.Id));
    }

    [Fact]
    public void CascadeDeletesChildren()
    {
        using var store = new LoahStore(_root, _options);
        var users = store.Collection<Person>("users");
        var orders = store.Collection<OrderRow>("orders");
        orders.SetSchema(new LoahCollectionSchema
        {
            References =
            {
                new LoahReferenceDefinition
                {
                    LocalField = nameof(OrderRow.UserId),
                    ReferencedCollection = "users",
                    OnDelete = LoahDeleteAction.Cascade,
                },
            },
        });

        var user = users.Insert(new Person { Name = "Ada", Email = "ada@example.com" });
        orders.Insert(new OrderRow { UserId = user.Id });
        Assert.True(users.Delete(user.Id));
        Assert.Equal(0, orders.Count);
    }

    [Fact]
    public void SetNullClearsChildReference()
    {
        using var store = new LoahStore(_root, _options);
        var users = store.Collection<Person>("users");
        var orders = store.Collection<OrderRow>("orders");
        orders.SetSchema(new LoahCollectionSchema
        {
            References =
            {
                new LoahReferenceDefinition
                {
                    LocalField = nameof(OrderRow.UserId),
                    ReferencedCollection = "users",
                    OnDelete = LoahDeleteAction.SetNull,
                },
            },
        });

        var user = users.Insert(new Person { Name = "Ada", Email = "ada@example.com" });
        var order = orders.Insert(new OrderRow { UserId = user.Id });
        Assert.True(users.Delete(user.Id));
        var reloaded = orders.GetById(order.Id);
        Assert.NotNull(reloaded);
        Assert.Null(reloaded!.UserId);
    }

    [Fact]
    public void MigrateToRollsBackOnFailure()
    {
        using var store = new LoahStore(_root, _options);
        Assert.Throws<InvalidOperationException>(() =>
            store.MigrateTo(2, (_, _) => throw new InvalidOperationException("boom")));

        var info = store.GetInfo();
        Assert.Equal(1, info.SchemaVersion);
    }

    [Fact]
    public void CheckIntegrityPassesForHealthyStore()
    {
        using var store = new LoahStore(_root, _options);
        var users = store.Collection<Person>("users");
        users.Insert(new Person { Name = "Ada", Email = "ada@example.com" });
        var report = store.CheckIntegrity();
        Assert.True(report.IsValid);
        Assert.True(report.DocumentsChecked >= 1);
    }

    [Fact]
    public void BackupZipContainsPageFile()
    {
        using var store = new LoahStore(_root, _options);
        store.Collection<Person>("users").Insert(new Person { Name = "Ada", Email = "ada@example.com" });
        var zip = Path.Combine(Path.GetTempPath(), "loah5-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            LoahBackup.Export(store, zip);
            using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
            Assert.Contains(archive.Entries, e => e.Name.EndsWith(".loahdb", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(zip))
            {
                File.Delete(zip);
            }
        }
    }

    [Fact]
    public void VacuumKeepsDataReadable()
    {
        using var store = new LoahStore(_root, _options);
        var users = store.Collection<Person>("users");
        users.Insert(new Person { Name = "Ada", Email = "ada@example.com" });
        store.Vacuum();
        Assert.Equal(1, users.Count);
        Assert.Equal("Ada", users.All()[0].Name);
    }

    private sealed class Person : LoahDocument
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
    }

    private sealed class OrderRow : LoahDocument
    {
        public string? UserId { get; set; }
    }
}
