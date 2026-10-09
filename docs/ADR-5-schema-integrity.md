# ADR-5: Phase 5 — Schema, integrity, vacuum, backup

## Status

Accepted (Phase 5)

## Context

Collections accepted any JSON document shape. Migrations could partially apply metadata. Backups omitted page-file databases. There was no structural verification API.

## Decisions

### Optional collection schema

`LoahCollectionSchema` (stored in the page catalog entry or JSON collection file) defines:

- **Required fields** — must be present and non-null on insert/update
- **Field types** — coarse types (`String`, `Int`, `Long`, `Double`, `Bool`, `DateTime`, `Object`)
- **References** — foreign-key-like rules to another collection (`Restrict`, `Cascade`, `SetNull` on parent delete)

Validation runs before indexes and persistence.

### Transactional migrations

`LoahStore.MigrateTo` runs the full version ladder inside a single `BeginTransaction()` / `Commit()` (or rollback on failure).

### Integrity

`CheckIntegrity()` returns a `LoahIntegrityReport`:

- Header magic and CRC
- Reachable page graph (no orphaned btree pages)
- Primary and secondary index entries resolve to existing document ids
- Reference constraints (child → parent) when schemas are registered

### Vacuum / compact

`Vacuum()` rewrites the page-file database into a minimal contiguous file (live pages only), then atomically replaces the original.

### Online backup

`LoahBackup.Export` checkpoints the page store (flush + WAL checkpoint), then zips the store folder **and** `{root}.loahdb` (+ `-wal` if present).

## Trade-offs

- Type checks use JSON token types after serialization (pragmatic, not full JSON Schema).
- `Cascade` deletes children in referencing collections loaded in the same store (by schema registration).
- Per-page trailing checksums are deferred; integrity uses header CRC + structural walks.

## Consequences

- New public types: `LoahCollectionSchema`, `LoahReferenceDefinition`, `LoahDeleteAction`, `LoahIntegrityReport`.
- `LoahCollection.SetSchema`, `LoahStore.CheckIntegrity`, `LoahStore.Vacuum`.
