# LoahDB

LoahDB is an embedded JSON document database for .NET. It keeps the original single-file `Loah<T>` API for simple key/value persistence, and adds a full document store for apps that want file-backed storage without running a database server.

## Features

| Capability | Description |
|------------|-------------|
| **Single-document store** | `Loah<T>` — one typed object per `.loah` file (unchanged API, now with async + atomic writes) |
| **Collections** | `LoahStore` + `LoahCollection<T>` — many documents per collection file |
| **Queries** | Fluent `Where`, `OrderBy`, `Skip`, `Take` over in-memory snapshots |
| **Indexes** | Unique or non-unique indexes on document properties |
| **Transactions** | Stage multiple writes, then commit or roll back |
| **Encryption** | Optional AES at the storage layer (shared with classic `Loah<T>`) |
| **Concurrency** | Per-file exclusive locks with configurable timeout |
| **Atomic writes** | Temp file + rename by default |
| **Backup / restore** | Zip export/import of an entire store |
| **TTL** | `ILoahExpiringDocument` + `PurgeExpired` |
| **Migrations** | Versioned schema migrations via `LoahStore.MigrateTo` |

## Quick start — classic API

```csharp
var settings = new Loah<AppSettings>("settings", "MyApp");
settings.Set(new AppSettings { Theme = "dark" });
var current = settings.Get();
```

## Quick start — document store

```csharp
var options = new LoahOptions
{
    BasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyAppData"),
    EncryptionKey = "optional-secret",
};

var store = new LoahStore("production", options);
var users = store.Collection<User>("users");

users.EnsureIndex("email", u => u.Email, unique: true);
users.Upsert(new User { Name = "Ada", Email = "ada@example.com" });

var ada = users.FindByIndex("email", "ada@example.com");
var adults = users.Query().Where(u => u.Age >= 18).OrderBy(u => u.Name).ToList();
```

### Transactions

```csharp
using var tx = store.BeginTransaction();
users.Insert(new User { Name = "A" });
users.Insert(new User { Name = "B" });
tx.Commit(); // or tx.Rollback()
```

### Backup

```csharp
LoahBackup.Export(store, "backup.zip");
LoahBackup.Import(store, "backup.zip", overwrite: true);
```

## When LoahDB is a good fit

- Desktop tools, utilities, and side projects that need structured persistence
- Prototypes and internal apps with moderate data size (collections load into memory)
- Offline-first clients that sync elsewhere later
- Scenarios where SQLite is overkill but `JSON` files alone are too raw

For heavy concurrent write load, very large datasets, or complex server-side queries, use a full database engine.

## Build & test

```bash
dotnet build LoahDB.sln
dotnet test LoahDB.sln
```

### Benchmarks (Phase 0 baseline)

Compare LoahDB, SQLite, and LiteDB (insert, point lookup, range/ordered query, update, delete):

```bash
dotnet run -c Release --project LoahDB.Benchmarks
```

Benchmarks use the current JSON-per-collection engine; later phases move to a page-based `.loahdb` file without changing the public API.

## Roadmap

| Phase | Focus |
|-------|--------|
| **0** (current) | Baseline bug fixes, BenchmarkDotNet harness |
| 1 | Page-based single-file storage (`.loahdb`) |
| 2 | WAL, ACID transactions, multi-process concurrency |
| 3 | Secondary B+Tree indexes |
| 4 | LINQ query planner |
| 5 | Schema, integrity, vacuum, online backup |
| 6 | Authenticated encryption (AES-GCM, KDF) |
| 7 | CLI tool, packaging, optional FTS |

See [docs/ADR-0-phase-0-baseline.md](docs/ADR-0-phase-0-baseline.md) for Phase 0 design notes.

## License

See [LICENSE.txt](LoahDB/LICENSE.txt).
