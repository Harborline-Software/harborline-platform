# Phase 2 baseline report (T-1053)

**Date:** 2026-10-03.
**Corpus:** `corpus-v1` (manifest `ebaacd5b…`).
**Data used:** only the training and validation splits. The three test splits are still sealed and open once, in Phase 3.

## Setup

- **Ordering seam.** `evaluation/SearchEngine.cs` is a pluggable-order copy of the exhaustive search. A policy can only choose the next activity and permute that activity's candidates. Every candidate stays reachable. Any fault (an exception, an invalid activity, or an ordering that is not a permutation) falls back to the incumbent order, and the fallback is counted.
- **Equivalence to production.** With `IncumbentPolicy`, the engine matches `DeterministicExhaustiveProofSolver` exactly on all 2,400 train and validation instances, at budgets of 1k, 50k and 5M. It agrees on status, work units, reason code and plan.
- **Budget accounting.** One work unit is one candidate consistency check, whether the search or a heuristic pays for it. Results are cached per search node, so nothing is charged twice. LCV's pairwise conflict probes are charged one unit each.
- **Budget calibration.** The incumbent engine runs single-threaded on 31 capped training instances. Its median throughput is 3,653 units/ms (p10 2,894, p90 4,856). So **0.5 s = 1,826,301 work units** and 5 s = 18,263,015. CP-SAT gets the same wall-clock allowance, model build included, with one worker and seed 0. Its 212 ms cold start is excluded from per-instance time and reported here instead.
- **What counts as solved.** A feasible plan that passes the independent checker, or a ProvenInfeasible result that the oracle confirms.

## Validation results at 0.5 s (600 instances)

| method | solved % | feasible found % | infeasible proved % | invalid accepted | false infeasible | fallbacks | wall p50 ms | wall p90 ms |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| incumbent | 94.0 | 95.1 | 89.8 | 0 | 0 | 0 | 0.9 | 68.5 |
| incumbent+dedupe | 94.0 | 95.1 | 89.8 | 0 | 0 | 0 | 1.3 | 66.1 |
| **dom-dynamic** | **98.8** | **100.0** | 94.5 | 0 | 0 | 0 | 1.6 | 4.7 |
| least-slack | 94.3 | 99.8 | 74.2 | 0 | 0 | 0 | 2.1 | 32.9 |
| dom-dynamic+lcv | 98.7 | 99.8 | 94.5 | 0 | 0 | 0 | 3.2 | 16.7 |
| greedy-then-search | 94.7 | 96.0 | 89.8 | 0 | 0 | 0 | 1.1 | 42.8 |
| cpsat (reported separately) | 100.0 | 100.0 | 100.0 | 0 | 0 | 0 | 4.4 | 8.1 |

Paired comparison against the incumbent, using a group bootstrap with 95% CIs:

- **dom-dynamic:** +4.8 points solved [+2.7, +7.3].
- **cpsat:** +6.0 points [+3.8, +8.5].
- **Work ratio:** dom-dynamic spends 10.1× [7.2, 13.6] the incumbent's work by geometric mean. It pays for counting domains at every node, which dominates the many easy instances. On hard instances it is far cheaper: wall p90 is 4.7 ms against 68.5 ms.

The full per-family table is in `evaluation/results/validation-0.5s.report.md`. Raw runs are in `evaluation/results/validation-0.5s.jsonl`.

## Findings

1. **Selected in-engine baseline: `dom-dynamic`.** It has the highest validation solved rate. This is the sdm-03 comparator for the model.
2. **No safety events.** No comparator accepted an invalid plan, made a false infeasibility claim, or triggered a fallback.
3. **Duplicate removal changes nothing here.** `incumbent+dedupe` is identical to the incumbent: the generated families never give one activity two requirements of the same capability. The compiler duplicate finding stays a characterisation only.
4. **There is little headroom on the solved-rate bar for seen families.** `dom-dynamic` finds a plan for 100% of feasible validation instances. All seven of its misses are infeasibility proofs, and value ordering cannot shorten a complete infeasibility proof. Only activity ordering can.
5. **The hard-case floor (sdm-03 item 6) is projected to fail.** This was estimated on pilot seeds, so the holdout is still sealed. `dom-dynamic` needs more than 1% of the budget, or fails, on 0% of shift-gaps small instances, 5% of shift-gaps medium, 4% of multi-skill small and 35.5% of multi-skill medium. Projected onto the 1,600-instance unseen-family holdout, that is about 180 such instances, against a required 300. As frozen, the experiment would very likely end INCONCLUSIVE, whatever the model does. For comparison, large unseen-family pilots are 33% (shift-gaps) and 72% (multi-skill) hard.
