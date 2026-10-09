# ADR-0: Phase 0 — Baseline fixes and benchmarks

## Status

Accepted (Phase 0)

## Context

LoahDB v1 stores each collection as one JSON file loaded entirely into RAM. The public API (`LoahStore`, `LoahCollection<T>`, `LoahQuery<T>`, etc.) is the contract for later storage-engine work. Several correctness and performance bugs were identified in the in-memory layer before building a page-based engine.

## Decisions

### Query snapshots

`LoahCollection<T>.Query()` returns a `LoahQuery<T>` bound to a **copy** of the document list at call time. Mutations to the collection after `Query()` do not affect an in-flight query. This matches snapshot semantics users expect and mirrors future MVCC-style reads.

### Document identity map

Maintain a `Dictionary<string, T>` (id → document) alongside the ordered document list. `GetById` and index resolution use the map (O(1)). The map is rebuilt on `Reload` and updated on insert/update/delete.

### InsertMany batching

`InsertMany` validates and adds all documents, rebuilds indexes once, and calls `Persist` once (unless inside a transaction, where staging is unchanged). Single-document `Insert` behavior is unchanged.

### Unique index failures are atomic

Before mutating in-memory state, unique constraints are checked. If `RebuildIndexes` fails, any partial adds from that operation are rolled back so count and indexes match the last committed state.

### OrderBy key selectors

`LoahQuery` compiles each `OrderBy` / `OrderByDescending` key selector **once** when the clause is registered, not on every comparison during sort.

### Thread-safe collection registry

`LoahStore` uses `ConcurrentDictionary` for the collection cache so parallel `Collection<T>(name)` calls are safe and return the same instance per key.

### Benchmarks

A `LoahDB.Benchmarks` project (BenchmarkDotNet) compares LoahDB, Microsoft.Data.Sqlite, and LiteDB on insert (1k/10k/100k), point lookup, range query, ordered query, update, and delete. Benchmarks are diagnostic only for Phase 0; targets for parity apply after Phase 1+.

## Trade-offs

- Query snapshots copy the document list (memory proportional to collection size at query time). Acceptable for v1 JSON collections; Phase 1 will avoid full-collection copies.
- Write-count testing uses an internal counter on `LoahStorage` (test assembly only via `InternalsVisibleTo`).

## Consequences

- All existing tests must pass unchanged.
- New tests prove snapshot semantics, unique-index rollback, single persist for `InsertMany`, and thread-safe collection access.
