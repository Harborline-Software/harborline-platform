# T-902 Kernel Core remainder, 2026-09-29

At Platform parent `0f308049` (after #195 added `KernelPackageClosure`), `node tooling/stryker.mjs baseline hlp.kernel.core.tests` with the repository-pinned Stryker.NET 5.0.0 and SDK `11.0.100-rc.1.26425.128` re-measured `Harborline.Kernel.Core` before any change: 361 mutants, 321 tested, 304 detected, 19 undetected (17 Survived, 2 NoCoverage), score **94.11 %**. That is the #195 figure unchanged; #185's 91.84 % predates `KernelPackageClosure`, whose mutants are all killed.

This PR adds one test, `CompiledBootstrapCatalogueTests.Identity_outside_the_floor_resolves_through_the_catalogue_reader`. No test before it resolved an identity outside the compiled floor, so the reader fallback at `CompiledBootstrapCatalogue.cs:129` had no coverage at all. Removing the fallback by hand (`: null;` in place of the reader call) turns the new test red (1 failed of 124), and restoring it turns it green.

After: 361 mutants, 322 tested, 304 detected, 19 undetected (18 Survived, 1 NoCoverage), score **94.11 %**. The line-129 mutant moved from NoCoverage to Survived because it is now covered, and it stays equivalent (below). No mutant outside the `ConfigureAwait` class can be killed, so the score and the floor stay where they are. The baseline row keeps `break` 94 and records the new `tested` count and commit; the config's thresholds (`break` 94, `low` 94, `high` 94) are unchanged. Break only rises (owner ruling 2026-09-26), and `floor(94.11)` is 94.

## Dispositions

| File:line:col | Mutator | Status | Disposition |
|---|---|---|---|
| `ConfigurationRecovery.cs:161:16` | Conditional (true): `committed.Committed` → `true` | Survived | **Equivalent: unreachable arm.** `KernelTransactionBoundary.ExecuteAsync` returns a refusal only when the batch holds a number of commands other than one (`KernelTransactionErrors.MultiCommandBatch`). A faulting port throws instead of refusing. `RecoverAsync` always passes the one-element collection `[new(operation, record, audit)]`, and `ExecuteAsync` is static, so no port or test double can make it refuse. `committed.Committed` is therefore always true where it is read. Applied by hand (`return (true)`): all 124 tests pass. |
| `ConfigurationRecovery.cs:163:47` | String: `"transaction-refused"` → `""` | NoCoverage | **Equivalent: unreachable arm.** Same reason: this is the false arm of line 161, which no input reaches. Applied by hand: all 124 tests pass. |
| `CompiledBootstrapCatalogue.cs:129:83` | Boolean: `ConfigureAwait(false)` → `true` | Survived (was NoCoverage) | **Equivalent: `ConfigureAwait` flip** (below). Now covered by the new test above, which passes under the flip when applied by hand. |
| `ConfigurationRecovery.cs:127:121`, `:130:101`, `:160:29` | Boolean: `ConfigureAwait(false)` → `true` | Survived | **Equivalent: `ConfigureAwait` flip.** |
| `KernelClock.cs:44:113` | Boolean: `ConfigureAwait(false)` → `true` | Survived | **Equivalent: `ConfigureAwait` flip.** |
| `KernelTransactionBoundary.cs:102:95`, `:105:75`, `:109:104`, `:110:98`, `:111:96`, `:112:89`, `:117:84`, `:140:29`, `:143:98`, `:144:96`, `:145:89`, `:150:84` | Boolean: `ConfigureAwait(false)` → `true` | Survived | **Equivalent: `ConfigureAwait` flip.** |

**`ConfigureAwait` flips (17): equivalent under a no-context caller constraint.** There is one in `CompiledBootstrapCatalogue`, three in `ConfigurationRecovery`, one in `KernelClock` and twelve in `KernelTransactionBoundary`. Together with the two unreachable `ConfigurationRecovery` arms, that accounts for all 19 undetected mutants. `ConfigureAwait(true)` differs from `ConfigureAwait(false)` only when an awaited host operation completes asynchronously while a `SynchronizationContext` (or a non-default `TaskScheduler`) is current. The continuation is then posted back to that context instead of running on the thread pool. Under that condition the flip is observable: a caller that blocks on the returned task from a single-threaded context would deadlock with `true`, and `false` prevents that. So the flips are equivalent under an explicit constraint, not unconditionally: Kernel Core is called asynchronously, from hosts that install no `SynchronizationContext` (ASP.NET Core, the xUnit runner, console and worker hosts). No production caller in `projections/dotnet` blocks on these methods (`.Result`, `.Wait()` or `GetAwaiter().GetResult()`), and the kernel installs no context itself. Under that constraint the awaited values, the staging order, rollback-on-fault and the returned results are the same under either argument. A host that breaks the constraint would need a test that installs a single-threaded context and blocks. That would test the hosting hazard `false` exists to prevent, not a Kernel Core contract, so it is left out of scope here.

Not counted in the score: 17 CompileError mutants and 21 Ignored (all "Block removal … already covered filter").

## Report

The unchanged raw [mutation report](mutation/t902-kernel-core-remainder-2026-09-29.json) (the after run) has SHA-256 `EC9AE3BF7A380AA5B642939723B56E3ABB53072CF21FF6BCF43E290EE24EB200`. Mutant ids change between runs, so match the rows above by file, line, column and mutator.

## Retiring the equivalents

The two `ConfigurationRecovery` equivalents would go away if the unreachable refusal arm were removed. The arm is defensive: if `ExecuteAsync` ever gains another refusal, it keeps recovery from reporting a refused transaction as committed. That call belongs to the owner, so this PR leaves the arm in place.
