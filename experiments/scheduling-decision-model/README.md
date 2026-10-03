# Scheduling decision model experiment

This folder is an isolated experiment for Control ticket **T-1053**. It tests whether a tiny learned scorer that only reorders the Scheduling exhaustive solver's exploration beats strong non-ML ordering under equal total budgets.

- No shipping project references anything here.
- The projects are deliberately not part of `Harborline.Platform.slnx`.
- Nothing here changes production solver semantics.

| Folder | Contents | Phase |
| --- | --- | --- |
| `docs/` | Profile spec, status mapping, decision report | 0–4 |
| `oracle/` | Independent plan checker and exhaustive enumerator | 1 |
| `data/` | Instance generator, manifests and frozen splits | 1 |
| `baselines/` | CP-SAT model (Google.OrTools, R-0139) and ordering heuristics | 1–2 |
| `tests/` | Hand-calculated cases, checker mutation tests, oracle cross-checks | 1 |
| `evaluation/`, `models/`, `replay/` | Budget harness, small scorer, accepted-output records | 2–3 |

## Commands

```bash
dotnet test experiments/scheduling-decision-model/tests
```

The CP-SAT runs use one worker (`num_workers = 1`) so results are reproducible on this host. A seed is recorded per run, but it does not guarantee identical results across runtimes or hardware.
