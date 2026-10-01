# T-1018 reactive dependency index: survivor dispositions, 2026-09-30

Ticket T-1018 (gap group G1 of the T-984 ck-7 re-run, `docs/evidence/T-984-ck7-rerun-2026-09-30.md`). This PR adds a reevaluate-equals-fresh differential test on both tiers, a Table-scoped Compute corpus case, and one TypeScript and one .NET test for field-prefixed dynamic reads. It found no production defect.

## Result

The differential test (200 seeded sequences of field edits, row add, row remove and row replacement, over Field, Row and Table-scoped Compute rules, an aggregate, Validate, Visibility and Options) passes on both tiers: `reevaluate` equals a fresh `evaluateInstance` after every edit. That is the evidence behind the equivalent dispositions below: the three recompute mechanisms (static reader index, recorded actual reads, demand evaluation of a dirty cell) each reproduce the answer the others give, so a mutant in one is masked and no input observes it.

Killed: Table-scoped Compute (TS `graph.ts` 519-522 and .NET `FormRuleGraph.cs` 407, corpus case `table/table-scoped-compute`, fails with `case 'Table'` disabled); field-prefixed dynamic reads (TS `graph.ts` 251 #300 and .NET `FormRuleGraph.cs` 854 #2980/#2982, test `tracks field-prefixed dynamic reads independently of their key list` and its .NET twin, red when the `field.` strip is removed). The other G1 survivors are equivalent, below.

Stryker over `graph.ts` (this run, after round 1 tests): 88 G1 survivors remained of 92. After the round 2 tests the full-package TS score is 71.87 percent (two runs, 72.04 and 71.87; before 59.34 recorded at the 2026-09-26 baseline, mostly from the T-984 tests). .NET rule-runtime: 63.11 percent (before 60.67); G1 lines went from 31 survivors to 24, all equivalent below.

## TypeScript `graph.ts` equivalents

| id | line | KILLED by test \| EQUIVALENT reason \| DEFECT |
|---:|---:|---|
| 291 | 247 | EQUIVALENT — root scopes always have both row coordinates null, so a row key misses and delegates to the same fallback. |
| 292 | 247 | EQUIVALENT — row section and id are created and omitted as a pair, making `||` and `&&` observationally identical here. |
| 293, 295 | 247 | EQUIVALENT — skipping the row guard still reaches the fallback after the non-existent root row-cell lookup. |
| 300 | 251 | KILLED by `tracks field-prefixed dynamic reads independently of their key list`. |
| 302 | 251 | EQUIVALENT — the altered fallback lookup has no computed target and resolves the same raw field. |
| 303 | 251 | EQUIVALENT — testing the empty prefix then slicing zero leaves the field name unchanged. |
| 306 | 258 | EQUIVALENT — every accepted aggregate reference installs its fold cell during build, so its resolver fallback is unreachable. |
| 353, 355 | 335 | EQUIVALENT — the second propagation loop consumes the unchanged initial queue. |
| 359 | 338 | EQUIVALENT — adding an already-dirty entry only changes queue work, not the dirty set. |
| 371, 375, 376 | 345, 347 | EQUIVALENT — clock seeding duplicates existing dirty-front propagation and demand evaluation returns the same cells. |
| 380, 384, 385, 386, 381, 382, 383 | 349, 350 | EQUIVALENT — the static clock-reader traversal is an optimisation; demand evaluation and plan selection retain the same observable values. |
| 389 | 359 | EQUIVALENT — evaluating clean ordered cells reuses their stable cached value. |
| 402, 403 | 363 | EQUIVALENT — static-plan-read filtering is duplicated by recorded actual plan reads and dirty targets. |
| 530 | 470 | EQUIVALENT — replacing an aggregate-cell descriptor for the same key during build does not change its fold definition. |
| 545 | 480 | EQUIVALENT — this build-time empty-array fallback only creates advisory edges; `foldAggregate` independently reads the real table. |
| 546, 548 | 482 | EQUIVALENT — boundary edge construction is advisory; fold bounds and rebuild-on-row-edit determine results. |
| 555, 554 | 486 | EQUIVALENT — aggregate row edges affect ordering only; aggregate demand evaluates its row producer directly. |
| 556 | 489 | EQUIVALENT — every non-fold cell at this branch has a non-null rule. |
| 560, 564 | 492, 493 | EQUIVALENT — the static reader index is duplicated by actual reads made by the single evaluator. |
| 606, 608, 607, 609, 610 | 541, 543 | EQUIVALENT — plan static reads are redundant with actual-plan reads for evaluated paths. |
| 618, 620 | 549 | EQUIVALENT — field reference resolution is duplicated by demand-time resolver reads. |
| 622, 623, 624, 625, 629, 626, 630, 631, 627, 628, 632 | 550 | EQUIVALENT — valid row plans always supply both row coordinates; demand-time row resolution supplies the same dependency. |
| 633 | 551 | EQUIVALENT — accepted aggregate references have a graph fold cell and demand resolves it independently. |
| 645 | 566 | EQUIVALENT — duplicate reader-set allocation changes only index storage, not the evaluated graph. |
| 653, 655 | 571 | EQUIVALENT — static and dynamic reader collections are redundant dependency frontiers. |
| 667, 669, 670, 671, 672, 673, 674, 675 | 579–584 | EQUIVALENT — stale dynamic reverse-index cleanup can add work, but each current evaluation re-records reads and preserves outcomes. |
| 678, 684 | 589, 592 | EQUIVALENT — current-generation evaluator recording duplicates the static/demand dependency path. |
| 689, 690, 692, 694, 823 | 596–603, 695 | EQUIVALENT — missing plan-read initialization falls back to a new set in `recordActualPlanRead`; only index allocation changes. |
| 709, 713, 717 | 614, 617, 620 | EQUIVALENT — accepted graphs are acyclic and demand evaluation makes topological ordering non-observable. |
| 722, 723, 718, 719, 720 | 621 | EQUIVALENT — compiler cycle admission rejects static cycles before graph construction, so the cyclic append is defensive dead code. |
| 726, 732 | 626, 627 | EQUIVALENT — bypassing a clean cached value recomputes the same deterministic cell. |
| 761, 762 | 651, 652 | EQUIVALENT — `DemandState` is discarded after the evaluation, so active-set cleanup has no later observable reader. |
| 793, 794, 795, 792 | 669 | EQUIVALENT — static cycle admission makes `c.cyclic` unreachable for publicly constructible graphs. |
| 819, 820 | 692 | EQUIVALENT — Compute cells are evaluated before Compute plans project their already-present value. |

## .NET `FormRuleGraph.cs` equivalents

- 119, 135, 144 (`#2540`, `#2548`, `#2549`, `#2557`, `#2558`): clock-reader dirty seeding and the dirty-queue traversal. Demand evaluation recomputes any dirty or missing cell on read, so the static traversal is an optimisation, the same as TS 335-350.
- 306-310 (`#2643`-`#2647`): the collection clears in `Build()`. `Build()` runs once, from the constructor, on freshly created collections, so the clears remove nothing. No rebuild path exists, so the ticket's "collection clears on rebuild" cannot differ.
- 354 (`#2670`), 360 (`#2676`): the aggregate row-edge bound and the aggregate edge insertion. The edges only order evaluation; `FoldAggregate` reads the real table and aggregate demand evaluates its row producers directly (TS 482-486).
- 445 (`#2703`-`#2705`): the row-reference guard in `ResolveRefKey`. A row-scoped plan always has a row section and row id, and demand-time resolution supplies the same dependency (TS 550).
- 477-495 (`#2724`, `#2728`, `#2732`, `#2736`): stale actual-read and reverse-index cleanup. Each evaluation re-records its reads, so a stale entry only adds work (TS 579-603).
- 561 (`#2782`): the plan-read initialisation. `RecordActualPlanRead` creates the set when missing (TS 692-695).
- 807 (`#2958`): `_active.Remove` in a `finally`. The demand state is discarded after the evaluation, so the cleanup has no later reader (TS 651-652).
- 850 (`#2974`): the `row.` path guard in `DemandResolver.ResolveVar`. A row-scoped scope always carries both coordinates, and a row-less scope falls through to the same fallback resolver.
