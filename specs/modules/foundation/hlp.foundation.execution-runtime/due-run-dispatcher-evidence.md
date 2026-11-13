# T-528/due-run-dispatcher verification

Control producer: `2c983d1e3dc7009cedf7ee67cbfe36e5e5c2b9ee`; merged slice equals the reviewed #1262 diff. Platform base `3ccc7571` includes #462. Owned ids: `execution-runtime-eng-3`, `execution-runtime-ck-14`.

## Measured mutation evidence

Native command: `node tooling/stryker.mjs run execution-runtime`. Repository report assertion: **147 tested, 94.55%, break 94, PASS**. No baseline, threshold or mutation exclusions changed. Report SHA-256: `3241d3c5d3f0b42d2324dd4d8254795cfafe8403916b555c12fc3e05bf7d2e6b`. Local report directory: `hlp.foundation.execution-runtime.tests-70RNLg` (archived outside the checkout after inspection).

The table names actual killing tests from the JSON report; ids are run-local, so match by file, line and mutator. Theory rows cover the named method; the report supplies individual case ids. All test display names include their full owned item id.

| Killing test | Source / line | Named killed mutation (run-local id) |
| --- | --- | --- |
| `Retry_claim` | `DueRunDispatcher.cs:49` | Equality mutation → `due < _clock.GetUtcNow()` (`22`) |
| `Missing_handler` | `DueRunDispatcher.cs:50` | Linq method mutation (FirstOrDefault() to First()) → `_registry.Registrations.First` (`25`) |
| `Timer_boundary_and_concurrency` | `DueRunDispatcher.cs:65` | Logical mutation → `wait is null &#124;&#124; wait.Reason != RunWaitReason.Timer &#124;&#124; wait.IsHeld &#124;&#124; wait.WakeUpUtc > now && wait.ClaimExpiresUtc > now` (`47`) |
| `Lease_and_fence` | `InMemoryRunStore.cs:45` | Logical mutation → `current.Version != expectedVersion &#124;&#124; record.Version != expectedVersion + 1 && !_runs.TryUpdate(key, Snapshot(record), current)` (`373`) |
| `Restart_recovers_own_claims` | `DueRunDispatcher.cs:109` | Boolean mutation → `false` (`91`) |
| `Hold_release` | `DueRunDispatcher.cs:65` | Logical mutation → `wait is null &#124;&#124; wait.Reason != RunWaitReason.Timer &#124;&#124; wait.IsHeld &#124;&#124; wait.WakeUpUtc > now && wait.ClaimExpiresUtc > now` (`47`) |
| `Invalid_settings` | `DueRunDispatcher.cs:23` | Statement mutation → `;` (`1`) |
| `Startup_conflict` | `DueRunDispatcher.cs:112` | Logical mutation → `run is null && !run.Waits.Any(wait => wait.Reason == RunWaitReason.Timer && wait.ClaimDispatcherId == _dispatcherId)` (`94`) |
| `No_context_capture` | `DueRunDispatcher.cs:42` | Boolean mutation → `true` (`15`) |
| `Listed_timer_changed` | `DueRunDispatcher.cs:62` | Logical mutation → `run is null && run.Status != RunStatus.Running` (`40`) |
| `Claim_conflict_and_handler_error` | `DueRunDispatcher.cs:77` | Statement mutation → `;` (`72`) |
| `Parallel_timer_resolution` | `DueRunDispatcher.cs:64` | Linq method mutation (FirstOrDefault() to First()) → `run.Waits.First` (`45`) |
| `Queued_pass` | `DueRunDispatcher.cs:37` | Statement mutation → `;` (`9`) |
| `Retry_conflict` | `DueRunDispatcher.cs:53` | Equality mutation → `claimed is not null` (`32`) |
| `Invalid_waits` | `RunRecord.cs:93` | Statement mutation → `;` (`545`) |
| `RunWaitsTests.Parallel_waits` | `InMemoryRunStore.cs`, Snapshot | Manual named mutant `drop-wait-snapshot`: replace `Waits = record.Waits.ToArray()` with `Waits = []`; filtered native test failed (1 failed, 0 passed), then source restored. |

Oracles: ADR-0118 decisions 1–3 and 6; ADR-0132 decisions 3 and 5; ADR-0143 decisions 1–3; ADR-0147 decisions 1–4. Assertions use literal UTC boundaries, statuses and counts, single-claim and unchanged-state properties. Context tests reuse T-1039’s no-capture property. The resolving handler is a minimal engine stand-in over the production version-checked store; no engine or production wait resolver is added.

## Source binding

SHA-256 of source text with LF newlines and one final newline. The report source matches after removing extra blank lines at EOF (the only cleanup after measurement):

- `DueRunDispatcher.cs`: `c011a5fa024cee11b14757a00b33573a567a2bcac581a1601b1868b88e3f6a70`
- `InMemoryRunStore.cs`: `aef20b964946139d29890365eb773654e081a6745a6b22c4fafb99624f6c175d`
- `RunKindRegistry.cs`: `48acfa9901330ae89e304a35dc9e0e4dfc37217ea33e5d14b6f9e2f61721c57a`
- `RunRecord.cs`: `711fd0e7b1f12139f9f8499573b270d4062cea2eb53fed2157f0a7e3fbc1de17`

## Limits

Three survivors remain: retry-refusal catch block removal (equivalent return-null instrumentation), cleanup success `break` removal (extra read, same stored outcome), and validation diagnostic empty-string mutation. Five compile-error mutants are reported by Stryker; the dispatcher body itself was instrumented and exercised. This is local native evidence, not protected merge-group or main verification. Attempt timeout, durable storage, engine adoption, human deadline resolution and lease renewal remain outside this slice.

Final local checks: repository `node tooling/run-native.mjs --build` PASS (11 steps); `dotnet test projections/dotnet/foundation/hlp.foundation.execution-runtime.tests --configuration Release --no-restore` PASS (168 tests, zero skipped); `node tooling/run-pr-preflight.mjs` PASS including catalog/provenance. Mutation diagnostics were archived outside the checkout before preflight, because their captured logs contain local protocol strings the scanner correctly rejects.
