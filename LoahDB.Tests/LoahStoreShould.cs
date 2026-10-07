namespace LoahDB.Tests;

public class LoahStoreShould : IDisposable
{
    private readonly string _root = "StoreTests_" + Guid.NewGuid().ToString("N");
    private readonly LoahOptions _options;

    public LoahStoreShould()
    {
        _options = new LoahOptions
        {
            BasePath = Path.Combine(Path.GetTempPath(), "LoahDBTests"),
            StorageFormat = LoahStorageFormat.JsonCollections,
        };
    }

    public void Dispose()
    {
        var path = Path.Combine(_options.BasePath, _root);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private LoahStore CreateStore() => new LoahStore(_root, _options);

    [Fact]
    public void InsertsAndQueriesDocuments()
    {
        var store = CreateStore();
        var users = store.Collection<UserDoc>("users");

        users.Insert(new UserDoc { Name = "Ada", Email = "ada@example.com", Age = 36 });
        users.Insert(new UserDoc { Name = "Grace", Email = "grace@example.com", Age = 85 });

        var result = users.Query()
            .Where(u => u.Age >= 40)
            .OrderBy(u => u.Name)
            .ToList();

        Assert.Single(result);
        Assert.Equal("Grace", result[0].Name);
    }

    [Fact]
    public void UpsertUpdatesExistingDocument()
    {
        var store = CreateStore();
        var users = store.Collection<UserDoc>("users");
        var doc = new UserDoc { Name = "Lin", Email = "lin@example.com" };
        users.Insert(doc);

        doc.Name = "Linus";
        users.Upsert(doc);

        Assert.Equal("Linus", users.GetById(doc.Id)!.Name);
    }

    [Fact]
    public void IndexFindsByEmail()
    {
        var store = CreateStore();
        var users = store.Collection<UserDoc>("users");
        users.EnsureIndex("by_email", u => u.Email, unique: true);
        var doc = users.Insert(new UserDoc { Name = "Ken", Email = "ken@example.com" });

        var found = users.FindByIndex("by_email", "ken@example.com");
        Assert.NotNull(found);
        Assert.Equal(doc.Id, found!.Id);
    }

    [Fact]
    public void TransactionCommitsAllWrites()
    {
        var store = CreateStore();
        var users = store.Collection<UserDoc>("users");

        using (var tx = store.BeginTransaction())
        {
            users.Insert(new UserDoc { Name = "A", Email = "a@example.com" });
            users.Insert(new UserDoc { Name = "B", Email = "b@example.com" });
            tx.Commit();
        }

        Assert.Equal(2, users.Count);
    }

    [Fact]
    public void TransactionRollbackDiscardsChanges()
    {
        var store = CreateStore();
        var users = store.Collection<UserDoc>("users");
        users.Insert(new UserDoc { Name = "Keep", Email = "keep@example.com" });

        using (var tx = store.BeginTransaction())
        {
            users.Insert(new UserDoc { Name = "Drop", Email = "drop@example.com" });
            tx.Rollback();
        }

        Assert.Equal(1, users.Count);
        Assert.DoesNotContain(users.All(), u => u.Name == "Drop");
    }

    [Fact]
    public void BackupRoundTripsStore()
    {
        var store = CreateStore();
        var users = store.Collection<UserDoc>("users");
        users.Insert(new UserDoc { Name = "Backup", Email = "backup@example.com" });

        var zipPath = Path.Combine(Path.GetTempPath(), "loah-backup-" + Guid.NewGuid().ToString("N") + ".zip");
        LoahBackup.Export(store, zipPath);

        var restoredRoot = _root + "_restored";
        var restoredStore = new LoahStore(restoredRoot, _options);
        LoahBackup.Import(restoredStore, zipPath, overwrite: true);

        var restoredUsers = restoredStore.Collection<UserDoc>("users");
        Assert.Single(restoredUsers.All());
        Assert.Equal("backup@example.com", restoredUsers.All()[0].Email);

        File.Delete(zipPath);
        var restoredPath = Path.Combine(_options.BasePath, restoredRoot);
        if (Directory.Exists(restoredPath))
        {
            Directory.Delete(restoredPath, recursive: true);
        }
    }

    [Fact]
    public void PurgeExpiredRemovesOldDocuments()
    {
        var store = CreateStore();
        var sessions = store.Collection<SessionDoc>("sessions");
        sessions.Insert(new SessionDoc
        {
            Token = "old",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5),
        });
        sessions.Insert(new SessionDoc
        {
            Token = "fresh",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
        });

        var removed = store.PurgeExpired<SessionDoc>("sessions");
        Assert.Equal(1, removed);
        Assert.Single(sessions.All());
        Assert.Equal("fresh", sessions.All()[0].Token);
    }

    private sealed class UserDoc : LoahDocument
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public int Age { get; set; }
    }

    private sealed class SessionDoc : LoahDocument, ILoahExpiringDocument
    {
        public string Token { get; set; } = "";
        public DateTime? ExpiresAtUtc { get; set; }
    }
}
