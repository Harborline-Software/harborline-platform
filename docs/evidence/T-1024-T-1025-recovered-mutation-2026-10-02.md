# T-1024 / T-1025 recovered mutation evidence

This corrects the mutation provenance of Platform #255 (source head
`f04a55c8c901bb363e1614588d1e33ad14eadd85`) and merged #254
(`c6816f644fb8a26cbaa0c1db21cfd0a8edfb684b`). It does not close either ticket
or certify `kernel-core-ck-7`.

## Exact report hashes

The recovered reports and the final T-1024 rerun are published on
`archive/pr255-pr254-mutation-reports-20261002`, at commit
`0a971613cdca53a7ef6831b3605df1a74f1b2b62`, under `docs/evidence/mutation/`,
following the owner's explicit approval of this public archive disclosure.
A fresh remote fetch verified all five report bytes and the manifest against
the locally prepared SHA-256 values and byte counts.

| Raw report | SHA-256 |
| --- | --- |
| [t1024-one-character-draft.json](https://github.com/Harborline-Software/harborline-platform/blob/0a971613cdca53a7ef6831b3605df1a74f1b2b62/docs/evidence/mutation/t1024-one-character-draft.json) | `c27430c6b1725b17adfbb9e57c39190ac4864b8ac2d32bd668f90bcfabc7b425` |
| [t1025-dotnet-before.json](https://github.com/Harborline-Software/harborline-platform/blob/0a971613cdca53a7ef6831b3605df1a74f1b2b62/docs/evidence/mutation/t1025-dotnet-before.json) | `ffde852330e753f367674112fd7f43ca0a7c435e6e2c15e5e0831722ed647074` |
| [t1025-dotnet-after-before-final-items2-assertion.json](https://github.com/Harborline-Software/harborline-platform/blob/0a971613cdca53a7ef6831b3605df1a74f1b2b62/docs/evidence/mutation/t1025-dotnet-after-before-final-items2-assertion.json) | `a91459f44d3dfac649f58bef7bf47b9a48e0957186188513ef428fb44f0b220f` |
| [t1025-typescript-before.json](https://github.com/Harborline-Software/harborline-platform/blob/0a971613cdca53a7ef6831b3605df1a74f1b2b62/docs/evidence/mutation/t1025-typescript-before.json) | `3c22f9fed3b379b7024d42741a302ad0b5a578987236f31bc08364a649108c2a` |
| [t1024-two-character-final-72bb0d63.json](https://github.com/Harborline-Software/harborline-platform/blob/0a971613cdca53a7ef6831b3605df1a74f1b2b62/docs/evidence/mutation/t1024-two-character-final-72bb0d63.json) | `fc04f12fd023cf281936356f01670b0d84c541c2ca94b3c46fd39a08395e48b4` |

The three previous .NET hash strings had 63 hexadecimal digits. GNU
`sha256sum` escaped these files' output with a leading backslash; cutting the
first 64 characters retained that marker and omitted the last hash digit. The
hashes above were recomputed directly over the file bytes, without parsing that
escaped display. No missing digit was inferred.

## T-1024: final two-character fixture rerun

The shared corpus case `work/single-key-object-literal-is-charged-as-data` at
the source head above uses `[{"kk":"v"}]`. The independent literal cost
calculation is `2 + (2 + (2 + 6*2 + 1 + (2 + 6*1))) = 27`; the Compute proof
is `2 * (1 + 2*27) = 110`, and a ceiling of 109 must refuse compilation.

The recovered scoped .NET report contains the earlier one-character fixture.
On `CoreWorkDerivation.cs:234-235`, IDs 594-598 are Killed, but ID 599,
Arithmetic mutation `6 * pair.Key.Length` to `6 / pair.Key.Length`, is
**Survived** with no killing test. Its reported 94.44% score is draft evidence,
not the score of the final two-character fixture. Prior hand-mutation claims
do not establish an exact-tree Stryker rerun.

After a normal merge of current main, source head
`72bb0d63eafbdab61da7d79b0d8a0ba743779e07` was run natively in the allocated
Windows slot with Stryker.NET 5.0.0 and pinned SDK
`11.0.100-rc.1.26425.128`. The since filter was disabled, the whole
`Compilation/CoreWorkDerivation.cs` file was selected, coverage analysis was
off, and the checked-in threshold remained `break: 63` (`low: 63`, `high: 80`).
No test filter was applied; Stryker ran the rule-runtime test project.

The run tested 234 mutants and scored **94.87%**, exiting successfully. Its
raw [final-fixture report](https://github.com/Harborline-Software/harborline-platform/blob/0a971613cdca53a7ef6831b3605df1a74f1b2b62/docs/evidence/mutation/t1024-two-character-final-72bb0d63.json) has
SHA-256 `fc04f12fd023cf281936356f01670b0d84c541c2ca94b3c46fd39a08395e48b4`.
The source `CoreWorkDerivation.cs` embedded in the
report matches that source head exactly after normalizing line endings.

All six arithmetic mutants on the single-key arm (report IDs 594-599 at lines
234-235) are **Killed**. The previously surviving mutant 599 is at line 235,
columns 42-61, replacement `6 / pair.Key.Length`. Its named killing tests are
`ConformanceTests.Corpus_case_matches_byte_identical` (report test ID
`d47f6313-34fa-758a-2906-162f4305eb8e`) and
`ConformanceTests.Emit_cross_tier_artifact` (test ID
`04bc0bc4-6a99-d66a-0a8a-6577d0692346`). Both names resolve from the raw report's
`killedBy` and `testFiles`. Other arm mutants are killed by one or both of those
tests. The report gives method-level test names; it does not name each theory
fixture argument separately.

The exact shared corpus file used for the run has SHA-256
`bf4bd53ca8a75918819ac162239f0db2f325287b01ac23b57e24254216a9a49f`; it contains the final two-character `kk` fixture. The
Stryker config and raw-report manifest are retained with the local result.
Project-wide instrumentation rolled back 717 mutants as CompileError and
ignored the out-of-scope mutants; those statuses supply no kill evidence.
This scoped result clears the final-fixture mutation gap, not the required
whole-project rerun/floor or aggregate ck-7 acceptance.

## T-1025: what the recovered .NET report actually proves

The recovered after report predates the final `items2` assertion. Named tests
below are resolved from the report's `killedBy` and `testFiles` records; IDs are
specific to this report and do not substitute for file/line/mutator matching.

| Report ID | FormRuleGraph.cs line | Mutation | Raw status | Named killing test |
| --- | ---: | --- | --- | --- |
| 2605 | 235 | remove `cts.CancelAfter(...)` | Survived | none |
| 2760 | 525 | empty `cell` error-parameter key | Survived | none |
| 2794 | 573 | empty `section` error-parameter key | Killed | `Corpus_case_matches_byte_identical` |
| 2806 | 595 | empty `cell` error-parameter key | Killed | `Corpus_case_matches_byte_identical` |
| 2860 | 633 | empty `section` error-parameter key | Killed | `Corpus_case_matches_byte_identical` |
| 2880, 2881 | 652 | empty `op` key / `agg` value | Killed | `Corpus_case_matches_byte_identical` |
| 2922 | 734 | change pending-work flag to true | Killed | `A_fail_closed_graph_reports_no_pending_work` |
| 2924 | 741 | empty `agg:` prefix | Killed | `An_over_limit_reactive_row_refuses_the_aggregate_naming_its_section_and_leaves_other_cells_alone` |
| 2925 | 741 | empty `/` prefix separator | Survived | none |
| 2926 | 742 | empty `section` error-parameter key | Killed | `An_over_limit_reactive_row_refuses_the_aggregate_naming_its_section_and_leaves_other_cells_alone` |
| 2942 | 775 | empty `cell` error-parameter key | Killed | `Corpus_case_matches_byte_identical` |

The final `items2` assertion addresses 2925, but this raw report cannot establish
its kill. The TS report is explicitly a before report; later manual mutations
are separate observations, not an after Stryker report.

Removing the wall-clock backstop is not universally equivalent to keeping it.
A step budget bounds logical work, not elapsed wall-clock time. A deterministic
backstop test or a justified timer seam remains an unfulfilled T-1025 obligation.
The compiler-cycle argument for the parameter-key survivor at line 525 is an
unproved reachability argument here, not a blanket equivalence certificate.
The same qualification applies to the corresponding TS cyclic-cell parameter.

## Validation and remaining acceptance

This repair preserves existing reports and adds the allocated native T-1024
scoped rerun above. It introduces no production or test behavior change and
claims no new full platform gate. Previous author-reported suite results remain historical.
No skipped JavaScript mutation job is credited to a C# change. Scoped scores do
not establish whole-project scores or justify changing whole-project floors;
the prescribed slice reruns, final-fixture kills, and ticket-owner equivalence
decisions remain separate requirements. No gate or threshold is weakened.
