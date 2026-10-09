# ADR-1: Phase 1 — Page-based single-file storage

## Status

Accepted (Phase 1)

## Context

Phase 0 fixed in-memory semantics over JSON-per-collection files. Phase 1 replaces full-file rewrite with a single `.loahdb` file per store: fixed 4096-byte pages, a B+Tree per collection (document id → JSON payload), overflow pages for large documents, a catalog B+Tree (collection name → metadata), and a free-page list.

## File format (v1 page store)

| Field | Location |
|-------|----------|
| Magic `LOAHDB\0` | Header page bytes 0–7 |
| Format version | uint16 @ 8 |
| Page size | uint16 @ 10 (4096) |
| Page count | uint64 @ 12 |
| Free-list head | uint32 @ 20 |
| Catalog root page | uint32 @ 24 |
| Schema version | uint32 @ 28 |
| KDF salt (reserved) | 32 bytes @ 32 |
| Header CRC32 | uint32 @ 64 |

Non-header pages begin with a one-byte `PageType` (BTreeLeaf, BTreeInternal, Overflow, Catalog).

## Invariants

- Page size is fixed at 4096 for format v1.
- Document ids are unique within a collection B+Tree.
- Only dirty pages are flushed; the LRU cache bounds memory (`LoahOptions.PageCacheCapacity`).
- Legacy v1 folders (`{root}/_collections/*.loah`) are imported explicitly via `LoahStore.ImportLegacyV1()`.

## Trade-offs

- Secondary indexes remain in-memory and are rebuilt by scanning the collection B+Tree (Phase 3 moves them to on-disk trees).
- Encryption still uses the Phase 0 AES-CBC layer at the JSON file level for legacy mode; page files are plaintext until Phase 6.
- `Loah<T>` single-document files remain JSON `.loah` files under `{BasePath}/{root}/` for API compatibility; the document store uses `{BasePath}/{root}.loahdb`.

## Consequences

- `LoahOptions.StorageFormat` selects `JsonCollections` (legacy) or `PageFile` (default for new stores).
- `LoahCollection<T>` delegates to `ICollectionBackend` implementations.
