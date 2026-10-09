# ADR-3: Phase 3 — Secondary B+Tree indexes

## Status

Accepted (Phase 3)

## Context

Phase 1–2 store documents in a primary B+Tree and keep secondary lookups in an in-memory dictionary rebuilt by scanning the collection. That does not scale and cannot support ordered range scans on disk.

## Decisions

### On-disk secondary trees (page-file mode)

Each `LoahIndexDefinition` stores a `RootPageId` for a dedicated B+Tree:

| Index kind | B+Tree key | Value |
|------------|------------|--------|
| Unique | `Encode(sortKey)` | document id (UTF-8) |
| Non-unique | `Encode(sortKey) + "\0" + docId` | empty marker byte |

`Encode` produces a lexicographically sortable string for range scans (`FindByIndexRange`).

### JSON collection mode

Secondary indexes remain in-memory (`LoahCollectionData.Indexes`) but use the same key encoding and support composite paths, nested properties, and multikey extraction for consistency.

### Composite & nested keys

- **Nested:** property path `Address.City` from member expression chains (`u => u.Address.City`).
- **Composite:** `EnsureCompositeIndex` registers multiple paths; encoded key joins components with `\u001f`.
- **Multikey:** if the resolved value is `IEnumerable` (except `string`), each element is indexed separately.

### Maintenance

- `EnsureIndex` / `EnsureCompositeIndex` rebuilds the affected tree from existing documents.
- Insert / update / delete update secondary trees incrementally (page mode) or rebuild in-memory maps (JSON mode).
- Unique checks use secondary trees before mutating the primary tree.

### Range & order

`FindByIndexRange(index, min, max, descending)` scans the secondary B+Tree (or in-memory map) using encoded key comparison.

## Trade-offs

- Range scans filter after B+Tree enumeration (no separate internal iterator yet); acceptable for Phase 3.
- Composite indexes use string encoding; very large keys are capped at `LoahConstants.BTreeMaxKeyBytes`.

## Consequences

- `LoahIndexDefinition` gains `RootPageId` and `PropertyPaths`.
- New tests cover composite, nested, range, descending, unique, and transactional index updates.
