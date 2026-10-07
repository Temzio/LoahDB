using LoahDB.Cli;

namespace LoahDB.Tests;

public class LoahPhase7Should : IDisposable
{
    private readonly string _root = "Phase7_" + Guid.NewGuid().ToString("N");
    private readonly string _basePath;

    public LoahPhase7Should()
    {
        _basePath = Path.Combine(Path.GetTempPath(), "LoahDBTests");
    }

    public void Dispose()
    {
        var db = Path.Combine(_basePath, _root + ".loahdb");
        if (File.Exists(db))
        {
            File.Delete(db);
        }
    }

    [Fact]
    public void FullTextSearchFindsMatchingDocuments()
    {
        var options = new LoahOptions
        {
            BasePath = _basePath,
            StorageFormat = LoahStorageFormat.PageFile,
        };
        using var store = new LoahStore(_root, options);
        var articles = store.Collection<Article>("articles");
        articles.EnsureFullTextIndex("body", a => a.Body);
        articles.Insert(new Article { Title = "A", Body = "The quick brown fox" });
        articles.Insert(new Article { Title = "B", Body = "Lazy dog sleeps" });
        articles.Insert(new Article { Title = "C", Body = "Quick fox runs" });

        var hits = articles.SearchFullText("body", "quick fox");
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void CliInfoCommandPrintsMetadata()
    {
        var options = new LoahOptions { BasePath = _basePath, StorageFormat = LoahStorageFormat.PageFile };
        using (var store = new LoahStore(_root, options))
        {
            store.Collection<Article>("articles").Insert(new Article { Title = "Hi", Body = "text" });
        }

        using var writer = new StringWriter();
        var code = LoahCliApp.Run(new[] { "--path", _basePath, "--root", _root, "info" }, writer);
        Assert.Equal(0, code);
        Assert.Contains("articles", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CliCheckCommandReturnsSuccessForHealthyStore()
    {
        var options = new LoahOptions { BasePath = _basePath, StorageFormat = LoahStorageFormat.PageFile };
        using (var store = new LoahStore(_root, options))
        {
            store.Collection<Article>("articles").Insert(new Article { Title = "Hi", Body = "text" });
        }

        using var writer = new StringWriter();
        var code = LoahCliApp.Run(new[] { "--path", _basePath, "--root", _root, "check" }, writer);
        Assert.Equal(0, code);
        Assert.Contains("OK", writer.ToString());
    }

    private sealed class Article : LoahDocument
    {
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
    }
}
