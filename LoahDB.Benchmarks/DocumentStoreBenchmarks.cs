using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using LiteDB;
using LiteDbQuery = LiteDB.Query;
using LoahDB;
using Microsoft.Data.Sqlite;

namespace LoahDB.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class DocumentStoreBenchmarks
{
    private string _loahRoot = "";
    private string _sqlitePath = "";
    private string _liteDbPath = "";
    private LoahOptions _loahOptions = null!;
    private List<BenchmarkDocument> _seed = null!;

    [Params(1_000, 10_000, 100_000)]
    public int DocumentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "LoahDB.Benchmarks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        _loahRoot = Path.Combine(baseDir, "loah");
        _sqlitePath = Path.Combine(baseDir, "bench.sqlite");
        _liteDbPath = Path.Combine(baseDir, "bench.litedb");
        _loahOptions = new LoahOptions { BasePath = baseDir, AtomicWrites = true };
        _seed = Enumerable.Range(0, DocumentCount)
            .Select(i => new BenchmarkDocument
            {
                Id = i.ToString("D8"),
                Name = $"User{i}",
                Email = $"user{i}@example.com",
                Age = 20 + (i % 50),
                Score = i * 3,
            })
            .ToList();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        var baseDir = _loahOptions.BasePath;
        if (Directory.Exists(baseDir))
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Benchmark(Baseline = false)]
    public void LoahDB_Insert()
    {
        var store = new LoahStore(_loahRoot + "_insert", _loahOptions);
        var col = store.Collection<BenchmarkDocument>("docs");
        col.EnsureIndex("by_email", d => d.Email);
        col.InsertMany(_seed);
    }

    [Benchmark]
    public void Sqlite_Insert()
    {
        if (File.Exists(_sqlitePath))
        {
            File.Delete(_sqlitePath);
        }

        using var connection = new SqliteConnection($"Data Source={_sqlitePath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE docs (
              id TEXT PRIMARY KEY,
              name TEXT NOT NULL,
              email TEXT NOT NULL,
              age INTEGER NOT NULL,
              score INTEGER NOT NULL
            );
            CREATE INDEX idx_docs_email ON docs(email);
            """;
        cmd.ExecuteNonQuery();

        using var tx = connection.BeginTransaction();
        foreach (var doc in _seed)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText =
                "INSERT INTO docs (id, name, email, age, score) VALUES ($id, $name, $email, $age, $score)";
            insert.Parameters.AddWithValue("$id", doc.Id);
            insert.Parameters.AddWithValue("$name", doc.Name);
            insert.Parameters.AddWithValue("$email", doc.Email);
            insert.Parameters.AddWithValue("$age", doc.Age);
            insert.Parameters.AddWithValue("$score", doc.Score);
            insert.ExecuteNonQuery();
        }

        tx.Commit();
    }

    [Benchmark]
    public void LiteDB_Insert()
    {
        if (File.Exists(_liteDbPath))
        {
            File.Delete(_liteDbPath);
        }

        using var db = new LiteDatabase(_liteDbPath);
        var col = db.GetCollection<BenchmarkDocument>("docs");
        col.EnsureIndex(d => d.Email);
        col.InsertBulk(_seed);
    }

    [Benchmark]
    public void LoahDB_PointLookup()
    {
        var store = PrepareLoah();
        var col = store.Collection<BenchmarkDocument>("docs");
        var target = _seed[DocumentCount / 2].Email;
        _ = col.FindByIndex("by_email", target);
    }

    [Benchmark]
    public void Sqlite_PointLookup()
    {
        PrepareSqlite();
        using var connection = new SqliteConnection($"Data Source={_sqlitePath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        var target = _seed[DocumentCount / 2].Email;
        cmd.CommandText = "SELECT id, name, email, age, score FROM docs WHERE email = $email LIMIT 1";
        cmd.Parameters.AddWithValue("$email", target);
        using var reader = cmd.ExecuteReader();
        reader.Read();
    }

    [Benchmark]
    public void LiteDB_PointLookup()
    {
        PrepareLiteDb();
        using var db = new LiteDatabase(_liteDbPath);
        var col = db.GetCollection<BenchmarkDocument>("docs");
        var target = _seed[DocumentCount / 2].Email;
        _ = col.FindOne(LiteDbQuery.EQ("Email", target));
    }

    [Benchmark]
    public int LoahDB_RangeQuery()
    {
        var store = PrepareLoah();
        var col = store.Collection<BenchmarkDocument>("docs");
        return col.Query().Where(d => d.Age >= 40 && d.Age <= 45).Count();
    }

    [Benchmark]
    public int Sqlite_RangeQuery()
    {
        PrepareSqlite();
        using var connection = new SqliteConnection($"Data Source={_sqlitePath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM docs WHERE age >= 40 AND age <= 45";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Benchmark]
    public int LiteDB_RangeQuery()
    {
        PrepareLiteDb();
        using var db = new LiteDatabase(_liteDbPath);
        var col = db.GetCollection<BenchmarkDocument>("docs");
        return col.Count(LiteDbQuery.And(LiteDbQuery.GTE("Age", 40), LiteDbQuery.LTE("Age", 45)));
    }

    [Benchmark]
    public List<BenchmarkDocument> LoahDB_OrderedQuery()
    {
        var store = PrepareLoah();
        var col = store.Collection<BenchmarkDocument>("docs");
        return col.Query().OrderBy(d => d.Name).Take(100).ToList();
    }

    [Benchmark]
    public void Sqlite_OrderedQuery()
    {
        PrepareSqlite();
        using var connection = new SqliteConnection($"Data Source={_sqlitePath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, email, age, score FROM docs ORDER BY name LIMIT 100";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
        }
    }

    [Benchmark]
    public void LiteDB_OrderedQuery()
    {
        PrepareLiteDb();
        using var db = new LiteDatabase(_liteDbPath);
        var col = db.GetCollection<BenchmarkDocument>("docs");
        _ = col.Query().OrderBy(d => d.Name).Limit(100).ToList();
    }

    [Benchmark]
    public void LoahDB_Update()
    {
        var store = PrepareLoah();
        var col = store.Collection<BenchmarkDocument>("docs");
        var doc = col.GetById(_seed[0].Id)!;
        doc.Score += 1;
        col.Update(doc);
    }

    [Benchmark]
    public void Sqlite_Update()
    {
        PrepareSqlite();
        using var connection = new SqliteConnection($"Data Source={_sqlitePath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE docs SET score = score + 1 WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", _seed[0].Id);
        cmd.ExecuteNonQuery();
    }

    [Benchmark]
    public void LiteDB_Update()
    {
        PrepareLiteDb();
        using var db = new LiteDatabase(_liteDbPath);
        var col = db.GetCollection<BenchmarkDocument>("docs");
        var doc = col.FindById(_seed[0].Id);
        doc!.Score += 1;
        col.Update(doc);
    }

    [Benchmark]
    public void LoahDB_Delete()
    {
        var store = PrepareLoah();
        var col = store.Collection<BenchmarkDocument>("docs");
        col.Delete(_seed[^1].Id);
    }

    [Benchmark]
    public void Sqlite_Delete()
    {
        PrepareSqlite();
        using var connection = new SqliteConnection($"Data Source={_sqlitePath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM docs WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", _seed[^1].Id);
        cmd.ExecuteNonQuery();
    }

    [Benchmark]
    public void LiteDB_Delete()
    {
        PrepareLiteDb();
        using var db = new LiteDatabase(_liteDbPath);
        var col = db.GetCollection<BenchmarkDocument>("docs");
        col.Delete(_seed[^1].Id);
    }

    private LoahStore PrepareLoah()
    {
        var root = _loahRoot + "_prep";
        var store = new LoahStore(root, _loahOptions);
        var col = store.Collection<BenchmarkDocument>("docs");
        if (col.Count == 0)
        {
            col.EnsureIndex("by_email", d => d.Email);
            col.InsertMany(_seed);
        }

        return store;
    }

    private void PrepareSqlite()
    {
        if (File.Exists(_sqlitePath))
        {
            return;
        }

        Sqlite_Insert();
    }

    private void PrepareLiteDb()
    {
        if (File.Exists(_liteDbPath))
        {
            return;
        }

        LiteDB_Insert();
    }
}
