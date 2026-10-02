# T-1024 / T-1025 recovered mutation evidence

This corrects the mutation provenance of Platform #255 (source head
`f04a55c8c901bb363e1614588d1e33ad14eadd85`) and merged #254
(`c6816f644fb8a26cbaa0c1db21cfd0a8edfb684b`). It does not close either ticket
or certify `kernel-core-ck-7`.

## Exact report hashes

The recovered report bytes are prepared in local archive commit
`0b83710eb41b40445ab43dff790a4541807a2778` on `archive/pr255-pr254-mutation-reports-20261002`,
under `docs/evidence/mutation/`. Archive publication approval is pending because
the repository is public. No reachable remote archive is claimed yet.

| Raw report | SHA-256 |
| --- | --- |
| `t1024-one-character-draft.json` | `c27430c6b1725b17adfbb9e57c39190ac4864b8ac2d32bd668f90bcfabc7b425` |
| `t1025-dotnet-before.json` | `ffde852330e753f367674112fd7f43ca0a7c435e6e2c15e5e0831722ed647074` |
| `t1025-dotnet-after-before-final-items2-assertion.json` | `a91459f44d3dfac649f58bef7bf47b9a48e0957186188513ef428fb44f0b220f` |
| `t1025-typescript-before.json` | `3c22f9fed3b379b7024d42741a302ad0b5a578987236f31bc08364a649108c2a` |

The three previous .NET hash strings had 63 hexadecimal digits. GNU
`sha256sum` escaped these files' output with a leading backslash; cutting the
first 64 characters retained that marker and omitted the last hash digit. The
hashes above were recomputed directly over the file bytes, without parsing that
escaped display. No missing digit was inferred.

## T-1024: final two-character fixture still requires a run

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

Required follow-up: a scoped native Stryker run over
`Compilation/CoreWorkDerivation.cs` using the final fixture, with coverage
analysis off and the normal checked-in threshold retained. Read the JSON and
match that arithmetic mutant by file, location and replacement; record its
named killing test and final report hash. The shared Windows test slot must be
allocated before running it. T-1024's mutation acceptance remains open.

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

This repair reads existing reports and preserves their bytes. It introduces no
production or test behavior change and claims no new local suite, mutation run
or full platform gate. Previous author-reported suite results remain historical.
No skipped JavaScript mutation job is credited to a C# change. Scoped scores do
not establish whole-project scores or justify changing whole-project floors;
the prescribed slice reruns, final-fixture kills, and ticket-owner equivalence
decisions remain separate requirements. No gate or threshold is weakened.
