# T-772 .NET core-work survivors, 2026-09-29

PR #194 made two one-line .NET fixes in `projections/dotnet/foundation/hlp.foundation.rule-runtime/Compilation/CoreWorkDerivation.cs`. Until now their only evidence was the red-first .NET pins; they had never had a Stryker.NET run.

- Line 42, `DeriveGraph`: `Math.Max(1, limits.MaxTableRowsPerAggregate) * MoneyAddResultBytes`. An aggregate is charged at least one fold result when `maxTableRowsPerAggregate` is 0. The comment above it is line 41.
- Line 223, `Cat`: `count == 0 ? 2 : …`. A `cat` with no arguments is charged the two quote bytes of the empty string.

## Run

Stryker.NET 5.0.0 (the pinned `dotnet-stryker` tool) ran on SDK 11.0.100-rc.1.26425.128 through the repository tooling. The config now sets `"coverage-analysis": "off"`. The pinning theory `Compiler_pins_public_transfer_proofs_for_cross_tier_calibration` takes its cases from `InlineData`.

```
node tooling/stryker.mjs full rule-runtime.tests -- \
  --mutate '**/CoreWorkDerivation.cs{2107..2324}' --mutate '**/CoreWorkDerivation.cs{13992..14114}'
```

The two spans are the characters of lines 41–42 and line 223. Stryker created 4088 mutants and tested the 8 on those lines. All 8 were killed, none survived, and the score is 100 %.

## Kill table

| line | mutator | replacement | status | killing test (case) |
|---|---|---|---|---|
| 42 | Arithmetic | `Math.Max(1, …) / MoneyAddResultBytes` | Killed | `RuleEngineUnitTests.Compiler_pins_public_transfer_proofs_for_cross_tier_calibration` (`empty-table-aggregate`) |
| 42 | Linq method (Max to Min) | `Math.Min` | Killed | `RuleEngineUnitTests.Compiler_pins_public_transfer_proofs_for_cross_tier_calibration` (`empty-table-aggregate`) |
| 223 | Conditional (true) | `true ? 2 : …` | Killed | `RuleEngineUnitTests.Compiler_pins_public_transfer_proofs_for_cross_tier_calibration` (`cat`) |
| 223 | Conditional (false) | `false ? 2 : …` | Killed | `RuleEngineUnitTests.Compiler_pins_public_transfer_proofs_for_cross_tier_calibration` (`cat`) |
| 223 | Equality | `count != 0` | Killed | `RuleEngineUnitTests.Compiler_pins_public_transfer_proofs_for_cross_tier_calibration` (`cat`) |
| 223 | Arithmetic | `total + 2 - 6 * child.Result` | Killed | `RuleEngineUnitTests.Compiler_pins_public_transfer_proofs_for_cross_tier_calibration` (`cat`) |
| 223 | Arithmetic | `total - 2` | Killed | `RuleEngineUnitTests.Compiler_pins_public_transfer_proofs_for_cross_tier_calibration` (`cat`) |
| 223 | Arithmetic | `6 / child.Result` | Killed | `RuleEngineUnitTests.Compiler_pins_public_transfer_proofs_for_cross_tier_calibration` (`cat`) |

With coverage analysis off, the report's `killedBy` list for a mutant is incomplete. For `Math.Min` it names only the two `StaticWorkCeilingTests` that also fail. To get the killing case, each of the 8 mutants was also applied by hand, the suite (348 tests) was run, and the source was restored:

- Both line-42 mutants fail 25 tests. These include the `empty-table-aggregate` case, which pins the fix, and every default-limit case of both pin theories.
- Every line-223 mutant fails the `cat` case. Three of them (`true`, `- 6`, `6 /`) also fail `Compiler_bounds_table_compute_value_copied_through_aggregate_reference` and `Compiler_keeps_larger_table_bound_for_admitted_duplicate_target`.

No mutant survived on either line, so this PR needs no new test and no equivalence reason.

## Project score and ratchet

| run | config | tested | detected | undetected | score |
|---|---|---|---|---|---|
| recorded baseline (`c09cfcac`, 2026-09-26) | per-test coverage | 2563 | 1552 | 1374 | 53.04 |
| before: current main (`578038f`) | main's config, per-test coverage | 2654 | 1809 | 1174 | 60.64 |
| after: `node tooling/stryker.mjs baseline rule-runtime.tests` | `coverage-analysis: off` | 2983 | 1810 | 1173 | 60.67 |

The number tested rises because mutants that per-test coverage marked NoCoverage are now run against the whole suite. One of them is killed. Per the ratchet rule, `break` moves from 53 to the new floor, 60, in the same change. `low` = max(60, 60) = 60 and `high` = 80. `node tooling/stryker.mjs check` passes.

The unchanged raw scoped [mutation report](mutation/t772-dotnet-core-work-2026-09-29.json) has SHA-256 `CA48656A736C9E76E40E1C1D89B29B0CDA1C688CDFBBD99DEC14817D893128E6`. This covers the .NET core-work changes of T-772. The whole-package break, host composition and `kernel-core-ck-7` remain open.
