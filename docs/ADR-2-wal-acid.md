# ADR-2: Phase 2 — WAL, ACID transactions, concurrency

## Status

Accepted (Phase 2)

## Context

Phase 1 introduced a page-file engine with per-operation `Flush`. That is neither crash-safe nor atomic across collections. JSON collections staged writes in memory but committed files one-by-one.

## Decisions

### Write-ahead log

A sibling file `{store}.loahdb-wal` holds checksummed frames:

| Type | Payload |
|------|---------|
| Page | `pageId` (uint32) + 4096 page bytes |
| Header | 4096-byte header page |
| Commit | empty (marks a durable transaction boundary) |
| Checkpoint | empty (WAL truncated after successful apply) |

Commit sequence: append PAGE/HEADER frames → COMMIT frame → `Flush(true)` on WAL → apply frames to the main `.loahdb` → `Flush(true)` on main → CHECKPOINT + truncate WAL.

### Recovery

On open, replay the last committed WAL transaction (frames after the previous CHECKPOINT up to COMMIT) if the main file might not reflect it. Incomplete transactions (no COMMIT) are discarded.

### In-process write batching

While a store transaction is active, the page engine buffers dirty pages and defers durable I/O until `Commit`. `Rollback` drops dirty cache entries and reloads from disk.

### Cross-process concurrency

- **Writer:** exclusive lock file `{store}.loahdb.writer.lock` (retry until `LoahOptions.LockTimeout`, then `LoahConcurrencyException`).
- **Readers:** open the main database with `FileShare.Read`; they observe the last committed snapshot.

### Nested transactions

A second `BeginTransaction()` on the same store throws `InvalidOperationException` (no silent nesting).

### JSON collection commits

Pending JSON writes are written to temp files and renamed atomically on commit so either all collection files update or none do.

## Trade-offs

- WAL replay rewrites whole pages (simple, correct); checkpoint truncates WAL to bound recovery time.
- Reader snapshot isolation across processes is “last committed on open/read,” not MVCC versioning (Phase 2 scope).
- Crash-injection tests use an internal `FaultInjectingFileStream` hook on the WAL path only.

## Consequences

- `LoahTransaction` coordinates JSON staging and page-store WAL commit.
- Tests cover recovery, lock timeout, nested-tx error, and fault injection at WAL/fsync boundaries.
