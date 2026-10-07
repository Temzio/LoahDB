# ADR-4: Phase 4 — LINQ query planner

## Status

Accepted (Phase 4)

## Context

`LoahQuery<T>` compiled predicates on every `Where` and always scanned the snapshot. Phase 3 added secondary B+Trees but queries did not use them automatically.

## Decisions

### Expression-based planning

`Where`, `OrderBy`, and aggregate selectors keep `Expression` trees. Execution builds a `QueryPlan` (explainable) and only compiles predicates that cannot be served from indexes.

### Supported predicate shapes

`==`, `!=`, `<`, `<=`, `>`, `>=`, `&&`, `||`, `!`, null checks, `string.StartsWith`, `string.Contains`, and `values.Contains(member)` (IN).

### Index selection

| Pattern | Plan step |
|---------|-----------|
| `prop == constant` | `IndexSeek` when a single-field index matches the property path |
| `prop >= a && prop <= b` (or single bound) | `IndexRange` |
| `OrderBy(prop)` with matching index | `IndexRange` scan in sort order (asc/desc) |
| Otherwise | `CollectionScan` + in-memory `Filter` |

`Skip` / `Take` are pushed onto the plan after filters and sort.

### API extensions

- `Explain()` — human-readable plan text
- `Select`, `Count`, `Any`, `Sum`, `Min`, `Max`, `Average`
- `GroupBy<TKey>`
- `Join` — nested-loop with `IndexSeek` on the inner collection when an index matches the inner key

### Snapshots

`Query()` still binds a document snapshot at call time (ADR-0). The planner chooses index-backed **id lists** when possible, then hydrates documents from the snapshot (or collection) by id.

## Trade-offs

- Composite indexes are not used for planning yet (single-field indexes only).
- `Or` predicates generally force a collection scan.
- No `IQueryable` provider in this phase (optional stretch).

## Consequences

- `LoahQuery<T>` is constructed with `LoahCollection<T>` context for index metadata and hydration.
- Tests assert plan shapes via `Explain()` and result correctness.
