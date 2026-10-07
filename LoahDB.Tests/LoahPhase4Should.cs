namespace LoahDB.Tests;

public class LoahPhase4Should : IDisposable
{
    private readonly string _root = "Phase4_" + Guid.NewGuid().ToString("N");
    private readonly LoahOptions _options;

    public LoahPhase4Should()
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
    }

    [Fact]
    public void ExplainShowsIndexSeekForEquality()
    {
        using var store = new LoahStore(_root, _options);
        var users = store.Collection<User>("users");
        users.EnsureIndex("by_email", u => u.Email, unique: true);
        users.Insert(new User { Name = "Ada", Email = "ada@example.com", Age = 30 });

        var plan = users.Query().Where(u => u.Email == "ada@example.com").Explain();
        Assert.Contains("IndexSeek", plan);
    }

    [Fact]
    public void IndexRangeQueryReturnsMatchingDocuments()
    {
        using var store = new LoahStore(_root, _options);
        var users = store.Collection<User>("users");
        users.EnsureIndex("by_age", u => u.Age);
        users.Insert(new User { Name = "Young", Email = "y@example.com", Age = 20 });
        users.Insert(new User { Name = "Mid", Email = "m@example.com", Age = 40 });
        users.Insert(new User { Name = "Old", Email = "o@example.com", Age = 60 });

        var results = users.Query().Where(u => u.Age >= 35 && u.Age <= 55).ToList();
        Assert.Single(results);
        Assert.Equal("Mid", results[0].Name);
        Assert.Contains("IndexRange", users.Query().Where(u => u.Age >= 35 && u.Age <= 55).Explain());
    }

    [Fact]
    public void AggregationsAndProjectionWork()
    {
        using var store = new LoahStore(_root, _options);
        var users = store.Collection<User>("users");
        users.Insert(new User { Name = "A", Email = "a@example.com", Age = 10 });
        users.Insert(new User { Name = "B", Email = "b@example.com", Age = 20 });

        Assert.Equal(2, users.Query().Count());
        Assert.Equal(30, users.Query().Sum(u => u.Age));
        var names = users.Query().Select(u => u.Name).ToList();
        Assert.Equal(new[] { "A", "B" }, names.OrderBy(n => n));
    }

    [Fact]
    public void JoinUsesInnerIndex()
    {
        using var store = new LoahStore(_root, _options);
        var users = store.Collection<User>("users");
        var orders = store.Collection<Order>("orders");
        orders.EnsureIndex("by_user", o => o.UserEmail, unique: false);
        users.Insert(new User { Name = "Ada", Email = "ada@example.com" });
        orders.Insert(new Order { UserEmail = "ada@example.com", Total = 42 });

        var joined = users.Query().Join(
            orders,
            u => u.Email,
            o => o.UserEmail,
            (u, o) => u.Name + ":" + o.Total);

        Assert.Single(joined);
        Assert.Equal("Ada:42", joined[0]);
    }

    private sealed class User : LoahDocument
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public int Age { get; set; }
    }

    private sealed class Order : LoahDocument
    {
        public string UserEmail { get; set; } = "";
        public int Total { get; set; }
    }
}
