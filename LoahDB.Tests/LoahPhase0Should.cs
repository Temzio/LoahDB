namespace LoahDB.Tests;

public class LoahPhase0Should : IDisposable
{
    private readonly string _root = "Phase0_" + Guid.NewGuid().ToString("N");
    private readonly LoahOptions _options;

    public LoahPhase0Should()
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
    public void QueryUsesSnapshotIsolatedFromLaterMutations()
    {
        var store = CreateStore();
        var users = store.Collection<UserDoc>("users");
        users.Insert(new UserDoc { Name = "Ada", Email = "ada@example.com", Age = 30 });

        var query = users.Query().Where(u => u.Age >= 18);
        users.Insert(new UserDoc { Name = "Child", Email = "c@example.com", Age = 10 });

        var results = query.ToList();
        Assert.Single(results);
        Assert.Equal("Ada", results[0].Name);
    }

    [Fact]
    public void InsertManyPersistsOnce()
    {
        var store = CreateStore();
        var users = store.Collection<UserDoc>("users");
        var writesBefore = store.Storage.WriteInvocationCount;

        users.InsertMany(Enumerable.Range(0, 50).Select(i => new UserDoc
        {
            Name = $"User{i}",
            Email = $"user{i}@example.com",
            Age = i,
        }));

        var writesAfter = store.Storage.WriteInvocationCount;
        Assert.Equal(writesBefore + 1, writesAfter);
        Assert.Equal(50, users.Count);
    }

    [Fact]
    public void FailedUniqueIndexInsertDoesNotModifyCollection()
    {
        var store = CreateStore();
        var users = store.Collection<UserDoc>("users");
        users.EnsureIndex("by_email", u => u.Email, unique: true);
        users.Insert(new UserDoc { Name = "First", Email = "dup@example.com" });

        Assert.Throws<InvalidOperationException>(() =>
            users.Insert(new UserDoc { Name = "Second", Email = "dup@example.com" }));

        Assert.Equal(1, users.Count);
        Assert.Equal("First", users.FindByIndex("by_email", "dup@example.com")!.Name);
    }

    [Fact]
    public void OrderByCompilesKeySelectorOncePerClause()
    {
        var counter = new OrderByInvocationCounter();
        var docs = new List<UserDoc>
        {
            new() { Name = "B", Email = "b@example.com", Age = 2 },
            new() { Name = "A", Email = "a@example.com", Age = 1 },
            new() { Name = "C", Email = "c@example.com", Age = 3 },
        };

        new LoahQuery<UserDoc>(docs).OrderBy(u => counter.Selector(u)).ToList();
        Assert.Equal(3, counter.Invocations);
    }

    [Fact]
    public void CollectionRegistryIsThreadSafe()
    {
        var store = CreateStore();
        const int threads = 16;
        var instances = new LoahCollection<UserDoc>?[threads];
        Parallel.For(0, threads, i =>
        {
            instances[i] = store.Collection<UserDoc>("shared");
        });

        var first = instances[0];
        Assert.NotNull(first);
        foreach (var instance in instances)
        {
            Assert.Same(first, instance);
        }
    }

    private sealed class UserDoc : LoahDocument
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public int Age { get; set; }
    }

    private sealed class OrderByInvocationCounter
    {
        public int Invocations { get; private set; }

        public string Selector(UserDoc doc)
        {
            Invocations++;
            return doc.Name;
        }
    }
}
