# Profile `sdm.unit-resource.v1`: written specification

Control ticket: T-1053. This document is the source of truth for the independent oracle in `../oracle/` and the CP-SAT model in `../baselines/`. Neither may call `FiniteCandidateCompiler`, `AssignmentRules` or any production solver to decide feasibility. Production record types (`SchedulingProfile`, `AssignmentCandidate`) are used only as data shapes.

## Inputs

A profile has activities, resources, requirements, start windows and precedence constraints, as in `SchedulingProfile` at Platform `23bb7175`. All five fact sets must be pinned complete. Otherwise the oracle reports **Unknown** and never Infeasible.

## Time

- Time is in integer slots.
- A placement of activity *a* with start *s* occupies the half-open interval `[s, s + d(a))`, where `d(a) >= 1`.
- The start window `(E, L)` bounds **the start** inclusively: `E <= s <= L`. The end may fall after `L`.
- There is no separate horizon. Windows and resource availability bound the problem.

## Rules a complete plan must satisfy

| Code | Rule |
| --- | --- |
| `R1-coverage` | Every activity has exactly one placement. No placement names an unknown activity. |
| `R2-duration` | `end = start + d(a)`. |
| `R3-window` | `E(a) <= start <= L(a)`. |
| `R4-resource-known` | Every resource ID in a placement names a profile resource, and a placement lists no resource twice. |
| `R5-requirements` | Let *Q* be the activity's requirements. The placement's resources can be split into disjoint groups, one per requirement *q*, of exactly `quantity(q)` resources that each have `capability(q)`. The placement lists no resource outside those groups, so its size is `Σ quantity(q)`. |
| `R6-availability` | Each placed resource lists every slot of `[start, end)` in `AvailableSlots`. |
| `R7-exclusive` | Two placements whose intervals intersect share no resource. Every resource is unit-capacity. |
| `R8-precedence` | For each constraint (before *b*, after *a*, gap *g*): `end(b) + g <= start(a)`. |

A plan is **feasible** when it satisfies `R1` through `R8`. Feasible makes no claim of optimality.

## Oracle outcomes

| Outcome | Meaning |
| --- | --- |
| `Feasible` | A witness plan was found and passed the checker. |
| `Infeasible` | The enumerator explored the complete placement space without pruning beyond `R1`–`R8`, and found no feasible plan. |
| `Unknown` | Incomplete pins or an exhausted enumeration cap. This is never treated as Infeasible. |

## Mapping to the experiment's per-result guarantee

The production `SolverGuarantee` is fixed per solver (see T-1053 Phase 0). The experiment records its own pair for each result:

| Status | Experiment guarantee |
| --- | --- |
| Feasible, checker passed | `FeasibleProof` |
| ProvenInfeasible from a complete search with no heuristic pruning | `InfeasibleProof` (experiment-only label) |
| Partial, Indeterminate, Cancelled | `None` |

## Explicitly not modelled

Cumulative capacity pools, calendars and time zones, recurrence, travel, soft objectives.
