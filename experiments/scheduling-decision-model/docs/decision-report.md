# Scheduling decision model: decision report (T-1053, Phase 4)

**Date:** 2026-10-03.
**Executor:** Claude (Opus 5.5) on the isolated Mac hub.
**Authority:** the owner's handoff (v2) and the T-1053 rulings.

## Decision and rationale

**NO GO for the learned search-order scorer.** The pre-registered criteria (T-1053 sdm-03) were applied mechanically by `evaluation decide`, which was committed before the holdout was opened.

- **Safety gates passed.** There were no invalid accepted plans, no false infeasibility claims and no fallbacks, for the model or for any comparator, across all 3,600 holdout instances.
- **Both primary endpoints failed** on the unseen-family holdout (n=2,400), compared with the strongest in-engine ordering (`dom-dynamic`):
  - (a) Solved difference: **−4.0 points** [−5.1, −3.0]. GO needed a lower bound of at least +10.
  - (b) Work ratio: **0.95×** [0.87, 1.04]. GO needed an upper bound of at most 0.5.
- **The no-regression rule failed.** The model is significantly worse on multi-skill (−8.0 points [−10.1, −6.1]) and on every larger-size family: jobshop −8.0, parallel-contention −9.0 and project-dag −11.5 points.
- **The hard-case floor was met.** 362 instances were hard for `dom-dynamic`, against the 300 required. The result is therefore a NO GO, not INCONCLUSIVE.

**Why the model failed.** It learned to imitate the "fewest consistent candidates" choice from cheap structural features, without paying for the exact counts. That pays off where its features track the true domain size. On the validation split (seen families) its work ratio was 0.81×. On the hard subset of the unseen-family holdout it was 0.55×, but with **−13.5 points solved**. On new structures, such as multi-skill activities that need two capabilities, and on larger sizes, the imitation picks worse activities. Exact counting does not make those mistakes. This is the distribution-shift failure the handoff warned about. The learned value (candidate) ordering was rejected on validation because it only added cost.

**Strongest comparator.** `dom-dynamic` (dynamic smallest-domain-first, a non-ML rule of about 40 lines) is the strongest in-engine ordering. CP-SAT is reported separately, as the owner directed.

## Run identity

| Item | Value |
| --- | --- |
| Platform source | pinned planning code at `23bb7175`; experiment commits `cca91e64` … `21d98a01` and this report |
| Control | T-1053, R-0139 |
| Profile | `sdm.unit-resource.v1` (`docs/profile-spec.md`) |
| Objective profile | none (feasibility only, by owner ruling) |
| Corpus | v2, manifest sha256 `d6916b48c35a40c3ead99445a36e25499aeec174431df553f4c082b41d4341c3`, 6,000 instances; generator `sdm-gen.v1`, master seed 20261003 |
| Splits | train 1,800 · validation 600 · test-seen 600 · test-unseen-size 600 · test-unseen-family 2,400 (shift-gaps, multi-skill) |
| Frozen model | `models/trained/frozen.json`: activity model sha256 `1bdd9d809ff6ee84e15e53ec49f9302975884750f714f981c19628287681ce91`, features `sdm-features.v1`, k=1, no value model |
| Charges | 2 units per scored activity or candidate; 1.25 per context-build operation (measured, rounded up) |
| Budget | 0.5 s = 1,826,301 work units (single-thread calibration on train); CP-SAT 500 ms wall, one worker, seed 0 |
| Runtime | .NET SDK 11.0.100-rc.1; Google.OrTools 9.15.6755 |
| Hardware | MacBook Pro 16,1, Intel i9-9880H (8 cores / 16 threads), 32 GB, macOS 26.7.1; runs used at most 8 threads |

## Evidence

**Unseen-family holdout at 0.5 s** (n=2,400; 1,940 feasible, 460 infeasible):

| Method | Solved % | Feasible found % | Infeasible proved % | Wall p50 ms | Wall p90 ms |
| --- | --- | --- | --- | --- | --- |
| incumbent (production today) | 70.8 | 70.8 | 70.4 | 8.2 | 921 |
| incumbent + dedupe | 71.0 | 71.1 | 70.7 | 8.4 | 933 |
| **dom-dynamic** | **95.0** | 98.0 | 82.0 | 2.4 | 44 |
| least-slack | 89.6 | 97.5 | 56.3 | 2.8 | 661 |
| dom-dynamic + LCV | 94.5 | 98.6 | 77.6 | 7.3 | 190 |
| greedy-then-search | 80.5 | 82.9 | 70.4 | 1.7 | 762 |
| **CP-SAT** | **99.9** | 100.0 | 99.6 | 7.7 | 20 |
| learned (frozen) | 90.9 | 94.9 | 74.1 | 2.1 | 420 |

**Paired comparisons with the incumbent** (group bootstrap, 95% CI):

| Comparator | Holdout | Solved difference vs incumbent | Work ratio vs incumbent |
| --- | --- | --- | --- |
| dom-dynamic | unseen family | +24.2 points [+22.1, +26.4] | 0.26× [0.21, 0.34] |
| dom-dynamic | unseen size | +21.2 points [+17.0, +25.3] | not reported |
| dom-dynamic | seen family | +3.7 points [+1.7, +6.0] | not reported |
| CP-SAT | unseen family | +29.2 points | not reported |
| CP-SAT | unseen size | +40.0 points | not reported |
| CP-SAT | seen family | +5.3 points | not reported |

**Model against `dom-dynamic`** (secondary): test-seen −1.0 points [−2.0, 0.0], work 0.85×.

**5 s secondary budget** (unseen family, reported but not decisive): the model against `dom-dynamic` is −2.8 points solved [−3.7, −2.0] with a work ratio of 1.01× [0.92, 1.12]. Multi-skill still regresses, at −5.3 points [−7.1, −3.7].

| Method | Solved at 5 s |
| --- | --- |
| incumbent | 76.8% |
| dom-dynamic | 97.2% |
| CP-SAT | 100.0% |
| learned | 94.4% |

The incumbent's p90 wall time of 8.0 s exceeds the nominal 5 s, because its throughput on multi-skill is below the train-calibrated rate. Work units, not wall time, bound the engine methods. Applied at 5 s, `decide` reads INCONCLUSIVE. The "hard" threshold scales with the budget (more than 1% of it), so only 217 instances qualify, against the 300 required. As a secondary budget, this does not change the 0.5 s verdict. See `evaluation/results/phase3-decision-5s-secondary.md`.

Every result file is under `evaluation/results/` (`phase3-*.jsonl`, `*.report.md`, `phase3-decision.md`).

## Safety

- **Invalid accepted plans:** 0, for every method on every split evaluated (validation and all holdouts).
- **False infeasibility claims:** 0, likewise.
- **Checker and oracle disagreements:** 0 across all 6,000 labels. Labels come from the enumerator (4,449), CP-SAT (1,550) and one `cpsat-600s` re-run.
- **Engine equivalence:** the engine with the incumbent ordering equals the production solver exactly on 2,400 instances at three budgets.
- **Fault tests:** scorer exceptions, non-permutations, unknown or assigned activities and NaN scores all fall back to the incumbent order with a truthful status (76 tests).
- **Replay:** not exercised. No output was accepted into any kernel or commit boundary, and nothing here touches production paths.

## Cost

| Item | Size |
| --- | --- |
| Experiment code | about 3,300 non-blank lines (oracle 336, baselines 135, data 801, engine 411, models 512, evaluation 607, tests 517) |
| Model artefact | 1.1 KB JSON |
| Training cost | 45 s (trace collection 10 s, fit about 17 s per L2 value) |
| Labelling cost | 599 s on 8 threads for v1, plus about 2 min for v2 |
| Dependencies | one new test-only dependency, Google.OrTools (R-0139); no ML library |
| Ongoing cost if adopted | a feature contract, a training pipeline, versioned model artefacts and drift monitoring. Not justified by the result. |

## Limitations

- Instances are synthetic, from five generated families. No real or representative data was used.
- The profile has unit-exclusive resources only: no cumulative capacity pools, calendars or objectives.
- Only one model class (linear) and one feature contract were tried. A richer model might generalise better, but the handoff asked for a tiny model. Also, `dom-dynamic` already leaves only 5 points of solved-rate headroom on unseen families, against CP-SAT's 99.9%.
- CP-SAT's infeasibility results are not independently certified beyond agreement with the enumerator on small cases.

## Counterexamples and failure artefacts

- Model regressions concentrate in `multi-skill/medium` (75.1% solved against 86.6% for `dom-dynamic`) and in large instances.
- Per-instance rows are in `evaluation/results/phase3-test-unseen-family-0.5s.jsonl`.

## Next step

No authorisation is requested for the model. Either of the following would need separate owner tickets; nothing has been done towards them here.

1. **Low-cost ordering fix.** Replace the production exhaustive solver's static fewest-candidates order with dynamic smallest-domain (`dom-dynamic`). This is a small change inside `PlanningSolvers.cs` that keeps exhaustive completeness and the existing proof semantics. Evidence: +24 points solved at 0.5 s on unseen families, and p90 wall 921 → 44 ms.
2. **Conventional solver.** Evaluate CP-SAT as a production solver for this profile. It solves 99.9% within 0.5 s on unseen families, but adopting it is a larger decision. It brings a runtime native dependency, and its infeasibility claims need a guarantee mapping (no independent certificate). The status/guarantee issue in Phase 0 finding 1 applies.
3. **Separate finding, still open:** `SolverGuarantee` is fixed per solver, so `Indeterminate` results carry `FeasibleProof`. See the Phase 0 report.
