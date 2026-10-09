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

### Benchmarks

Quick comparison table (10k docs, page-file engine):

```bash
dotnet run -c Release --project LoahDB.Benchmarks -- --quick-report docs/BENCHMARK-REPORT.md
```

Full BenchmarkDotNet suite (1k / 10k / 100k):

```bash
dotnet run -c Release --project LoahDB.Benchmarks
```

See [docs/BENCHMARK-REPORT.md](docs/BENCHMARK-REPORT.md).

## Roadmap

| Phase | Focus |
|-------|--------|
| 0 | Baseline bug fixes, BenchmarkDotNet harness |
| 1 | Page-based `.loahdb` store, B+Tree per collection, LRU page cache, `ImportLegacyV1` |
| 2 | WAL + fsync, atomic store transactions, writer lock, crash recovery |
| 3 | On-disk secondary B+Tree indexes (composite, nested, range, multikey) |
| 4 | Expression query planner, `Explain()`, aggregates, `Join` |
| 5 | Collection schema, referential actions, `CheckIntegrity`, `Vacuum`, page-file backup |
| 6 | PBKDF2 + AES-GCM payloads, legacy CBC read, `EnableEncryption`, `RotateEncryptionKey` |
| **7** (current) | `loah` CLI tool, NuGet 2.0, optional FTS, benchmark report |

Design notes: [ADR-0](docs/ADR-0-phase-0-baseline.md) … [ADR-7](docs/ADR-7-cli-packaging-fts.md).

### Page-file store (Phase 1)

New stores default to `LoahStorageFormat.PageFile` (`{BasePath}/{root}.loahdb`). JSON-per-collection layout remains available via `LoahStorageFormat.JsonCollections`. Import v1 data with `store.ImportLegacyV1()` after copying or creating `{root}/_collections/*.loah`.

Page-file commits use a write-ahead log (`{root}.loahdb-wal`) with checksummed frames and recovery on open. `BeginTransaction()` stages work across collections; a second nested `BeginTransaction()` throws. Writer exclusivity uses `{root}.loahdb.writer.lock` (see `LoahOptions.LockTimeout`).

Secondary indexes on page-file stores use per-index B+Trees (`EnsureIndex`, `EnsureCompositeIndex`, `FindByIndex`, `FindByIndexParts`, `FindByIndexRange`).

`LoahQuery` builds an index-aware plan (`Explain()`), supports `Select`, `GroupBy`, `Join`, and aggregates (`Count`, `Sum`, `Min`, `Max`, `Average`, `Any`).

### Schema & integrity (Phase 5)

`LoahCollection.SetSchema` enforces required fields, coarse types, and optional references (`Restrict`, `Cascade`, `SetNull` on parent delete). `LoahStore.CheckIntegrity()` validates the page file and loaded reference graphs. `Vacuum()` compacts the `.loahdb` file; `LoahBackup.Export` checkpoints the WAL and includes the page file in the zip.

### Encryption (Phase 6)

Set `LoahOptions.EncryptionKey` when creating a page-file store to encrypt catalog and document payloads (PBKDF2 + AES-GCM). JSON collection files use the `LOAH2:` authenticated format; legacy AES-CBC files still decrypt. Call `EnableEncryption()` to encrypt an existing plaintext page store, or `RotateEncryptionKey` to change the passphrase.

### CLI & FTS (Phase 7)

Install the global tool (local build):

```bash
dotnet pack LoahDB.Cli/LoahDB.Cli.csproj -c Release
dotnet tool install --global loah --add-source ./LoahDB.Cli/bin/Release
loah --path /data --root production info
```

Optional full-text indexes: `EnsureFullTextIndex` + `SearchFullText` (token AND queries).

NuGet library package: `dotnet pack LoahDB/LoahDB.csproj -c Release` → `LoahDB` 2.0.0.

## License

See [LICENSE.txt](LoahDB/LICENSE.txt).
