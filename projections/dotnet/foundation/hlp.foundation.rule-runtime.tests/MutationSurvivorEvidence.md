# T-493 answer-contract mutation dispositions

Production revision: `62227059c3ec14fbe452cf8df7a00b182da16b76` (current origin/main at final fetch). Every production file was restored byte-identically after hand seeding and matches that revision. Test source hashes below bind the measured test tree. Mutation runs used the repository's existing runners and configurations, with no full-project run or baseline/threshold change.

This inventory disposes every originally reported survivor/uncovered mutant in the four requested files. The owner's v1 ruling resolves the 42 previous design-call marks: 29 are now killed, 11 exception-prose mutants are ignored as non-contract text, and two numeric-fence mutants contradict documented tier parity. No open design-call mark remains. Contradictions are not equivalence or acceptance. Compile errors and timeouts are reported separately. Mutants are matched by file, source span, mutator and replacement, never by run-specific id.

## Final local verification

The foreground native gate passed all 13,531 tests with SDK 11.0.100. Before it, the touched suites passed separately: native Rule Runtime 615, Taxonomy 130, TypeScript Rule Runtime 646; TypeScript typecheck and all 86 architecture tests passed. Catalog preflight (`validate-repository.mjs --allow-stale-gate`) passed. Strict validation still reports four receipt errors, reproduced identically on clean main `62227059c3ec14fbe452cf8df7a00b182da16b76`: missing verified-gallery receipt, shared-result count mismatch, gallery count mismatch and unreconciled gallery scenarios. This is not full phase-4 qualification. Production, baseline and gate by-products are unchanged.

## Scoped scores and counts

| File | Before this round | After | Killed | Timeout | Survived | No coverage | Equivalent | Ignored prose | Contradicted | Compile error |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Native evaluator | 75.00% | 84.70% | 227 | 0 | 41 | 0 | 39 | 0 | 2 | 95 |
| Taxonomy interpreter | 89.72% | 90.19% | 193 | 0 | 21 | 0 | 10 | 11 | 0 | 203 |
| TS input envelope | 93.28% | 93.28% | 104 | 7 | 7 | 1 | 8 | 0 | 0 | 0 |
| TS reader | 95.96% | 96.86% | 216 | 0 | 7 | 0 | 7 | 0 | 0 | 0 |

Scores use detected / (detected + survived + uncovered), rounded to two decimals. The native wrapper truncates summaries: Taxonomy is 90.18 there versus 90.19 rounded. Timeouts are not killing-test evidence. The TS envelope score is unchanged: one previously timed-out mutant now reports Killed (104 killed / 7 timeout instead of 103 / 8). Runner Ignored entries outside the selected mutation scope are distinct from the 11 owner-ignored prose dispositions.

The original 209 targets are now 132 killed / 64 equivalent / 11 ignored non-contract text / 2 contradicted. By file: evaluator 71 / 39 / 0 / 2; Taxonomy 38 / 10 / 11 / 0; TS envelope 12 / 8 / 0 / 0; TS reader 11 / 7 / 0 / 0. No target lacks a disposition.

The adjacent native reader has 21 killed / 1 surviving / 86 compile errors (95.45%): its surviving Text catch removal is equivalent because RuntimeInputEnvelope refuses malformed surrogate strings before Text is called. It is listed separately from the four requested files.

## Oracle sources and the v1 ruling

- DES-0018 rules-ck-8 (line 66) and the owner's 2026-11-16 ruling: `{}` is falsy in harborline-jsonlogic/v1. Among empty containers, json-logic-js treats only `[]` as falsy. Any alignment is a v2 operator-set change requiring its own ADR.
- The same local literal corpus is embedded in each tier's permitted covering test file:

| Expression | Literal value |
|---|---|
| `{"!!":[{}]}` | `false` |
| `{"!":[{}]}` | `true` |
| `{"and":[{},true]}` | `{}` |
| `{"or":[{},true]}` | `true` |
| `{"if":[{},true,false]}` | `false` |

- DES-0018 compatibility (lines 219-221): legacy coding coercion remains. The numeric/collation corpus's independent literal examples and its ECMAScript canonical-number rule supply the numeric spelling/equality oracles; `value-states.json` supplies Pending and `rule.type_error` / `not-a-number` refusals.
- DES-0024 capture contract (lines 213-266): exact tuple shape, string tenant, ordinal tuple order, bounded writing, and the four capture refusal identities. Taxonomy interface line 43 supplies the literal 64-hop admission bound; taxonomy-eng-8 and the adopted reuse contract retain the existing interpreter's shared bound and refusal identities. Interface lines 51-59 document collection/overlay/refusal shapes, existing argument guards and independent flag bits.
- T-1143's fixture and RFC 7493 sections 2.1 and 2.3 supply the literal Unicode and duplicate-member refusals.
- The malformed-JSON cause test uses the owner's explicit cause ruling and ECMAScript JSON.parse's SyntaxError. No expected value is read from a production constant.

## Hand-seeded red and restored green

Each row was run against its hand seed, then passed with production restored byte-identically. Disjoint seeds used together affect different inputs and every corresponding case failed; they do not supply the Stryker counts, which come from the final JSON.

| Covering test/corpus | Hand seed | Red | Restored green |
|---|---|---|---|
| V1_empty_object_is_falsy_in_every_logical_operator and TS twin | Empty-object count comparison `> 0` to `>= 0` in each tier | Five failed per tier | Five passed per tier |
| Missing_operators_preserve_pending_dependencies | Remove Pending throw separately from missing and missing_some | Failed each run | Passed |
| Missing_some_preserves_the_bad_reference_identity | Remove missing_some's error throw | Failed | Passed |
| Legacy_coding_coercion_keeps_boolean_and_numeric_spellings | Force boolean spelling true; separately negate numeric formatting branch | Failed each run | Passed |
| Numeric_operations_preserve_the_recorded_nonnumeric_refusal | Replace nonnumeric reason with empty text | Failed | Passed |
| Nonfinite_arithmetic_fails_closed | Remove nonfinite guard | Failed | Passed |
| Money_refuses_non_decimal_values_instead_of_coercing_them | Negate long-operand check | Failed | Passed |
| Date_operators_refuse_invalid_dates_instead_of_returning_a_value | Return zero instead of date.add's refusal | Failed | Passed |
| Flat_loose_equality_coerces_numeric_strings_but_not_other_strings | Return false for numeric coercion; includes independent leading-zero control | Failed | Passed |
| Capture_stream_refuses_overflow_before_growing | Remove both span and array write guards | Both cases failed | Both passed |
| Duplicate_pinned_sources_refuse_instead_of_selecting_the_first | Remove duplicate-coordinate refusal | Failed | Passed |
| Descendants_admit_64_edges_and_refuse_the_65th | Change depth comparison to `>` | Failed | Passed |
| A_descendant_cycle_at_the_depth_boundary_keeps_its_cycle_identity | Remove root from descendant seen set; separately remove parent-walk starting node | Failed each run | Passed |
| Public_argument_guards_name_the_rejected_parameter | Remove constructor null guard | Failed | Passed |
| Expansion_refuses_malformed_inventories_and_collection_members | Bypass collection indexing | Failed | Passed |
| Known_vendor_designations_do_not_block_overlay_expansion | Treat any non-null designation as unknown | Failed | Passed |
| Overlay_without_a_vendor_reference_keeps_its_refusal_identity | Empty overlay refusal code | Failed | Passed |
| A_missing_parent_keeps_its_refusal_identity | Empty missing-parent refusal code | Failed | Passed |
| Diff_refuses_mismatching_tenant_or_definition_identity | Remove identity refusal | Failed | Passed |
| Missing-vendor expansion case | Change empty tenant sentinel | Failed | Passed |
| Adjacent-low-surrogate case | Remove high-surrogate upper bound | Failed | Passed |
| Four duplicate-member cases | Remove name-set insertion | Four failed | Four passed |
| Nontext argument / tuple arity / numeric tenant / tuple precedence cases | Remove corresponding reader checks at lines 15, 82, 87 and 108 | Four failed | Four passed |

Two literal direct seeds did not compile under nullable warnings-as-errors: the strict-null guard removal and the designation `&&` to `||` change. They are not counted as red tests. Compilable seeds of the same violated behavior were used for the reported red tests; no diagnostic suppression or runtime change was retained.

## Equivalence reference

The span-write equivalence was checked against maintained .NET 11 sources: [MemoryStream.Write(ReadOnlySpan<byte>)](https://github.com/dotnet/runtime/blob/v11.0.0/src/libraries/System.Private.CoreLib/src/System/IO/MemoryStream.cs). A derived MemoryStream delegates to Stream's span implementation, which invokes the byte-array override and preserves its bound.

## Owner disposition and record contradictions

The owner ruled that shipped v1 semantics and RuleError codes/parameters must be pinned where the tiers agree. Checked README's normative operator table and Decision DI (42-65), exact-money contract (84-90), DES-0018 rules-ck-7/8/10/12/20, both MoneyDecimal implementations and their public evaluator consumers, Taxonomy interface:58 and DES-0024:248-258. Control origin/main `986ca756b60ceddcef97603506b1b02136600ea1` has the same cited records as the local checkout. Traceability connections, ledger/design/ADR searches and the API rule-engine copy were also checked; none overrides these contracts.

- **Agreed semantics:** both permitted test files embed the same 19 expression/value literals for missing_some key coercion, string/numeric truthiness, boolean-to-number conversion and loose equality, serialized property-order-sensitive container equality, a runtime-null date unit falling back to day, and exact `0.005 * 3 = 0.015`. A statically null date unit is rejected by CoreTypeDerivation; the corpus deliberately supplies null through an owned runtime variable.
- **Money:** both corpora accept JSON `1.0` as an exact small integer (add one → `"2"`) and refuse `1.5` with `rule.type_error {op: money}`. MoneyDecimal has arbitrary decimal scale, not a currency/minor-unit parameter; README:84-90 specifies exact-or-fail-closed and no rounding mode. Independent literal charge calculations: unary five-digit addition is AST2 + mantissa5 = 7 steps; three-digit multiplication is AST3 + operand3 + operand3 + result5 = 14. Both exact-budget success and one-below refusal are pinned, including the rule parameter.
- **Diagnostics:** both corpora pin the complete single-parameter `rule.type_error` values for money/date, null-as-number and non-finite arithmetic. IEEE 754 overflow is refused, not blessed as a nonfinite numeric output. The TS reader pins malformed object bindings to `TypeError("Invalid pinned Taxonomy coding capture")` without a foreign iterator cause. Oracles are the owner's adopted v1 diagnostic literals and README RuleError/Decision DI, not production constants.
- **Precedence:** native ExpandCollection with both arguments null names `definition`; removing its first guard instead names collectionName. This native-only Taxonomy API has no TS interpreter counterpart to invent.
- **Ignored text:** exactly 11 targeted Taxonomy exception-prose mutants are `ignored: non-contract text`. That wording is not read by a contract or test; types, refusal codes and argument names remain covered. Other contractual message assertions are unchanged.

### Only record-contradicting cases

1. **ToMoney fence, HarborlineJsonLogic.cs:287:75 (`<` → `<=`).** Given owned input `{"coding":9.007e15}`, `{"money.add":[{"var":"coding"},"1"]}` refuses natively with `rule.type_error`, while TS resolves exact string `"9007000000000001"`. The exponent-form JSON number reaches native's double fence despite being an exactly representable integer. README Decision DI:61-65 and DES-0018 rules-ck-8:66 require identical semantics; MoneyDecimal's integer-input documentation and README:84-90 specify exact arithmetic, not representation-dependent refusal.
2. **NumNode storage fence, HarborlineJsonLogic.cs:423:35 (`<` → `<=`).** The same discrepancy appears when `{"+":[{"var":"coding"},0]}` feeds money.add: native stores the exact boundary result as double and ToMoney refuses it; TS resolves `"9007000000000001"`. The mutant stores it as long and changes the observable outcome. Same contradictory parity sources; not equivalent.

Temporary record-oracle probes reproduced both cases on unchanged main production: native 2 failed, TS 2 passed. The probes were removed rather than committing defect-pinning tests. No runtime change is permitted. These two cases, not the resolved design marks, are the only contract contradictions to escalate. No baseline edit or baseline proposal is included; only scoped scores are measured.

## Owner-round hand proofs

| Covering test/corpus | Individual hand seeds | Red | Restored green |
|---|---|---|---|
| V1_agreed_operator_semantics_match_literal_parity_corpus and TS twin | missing_some null key fallback; date day fallback; string/number truthiness; true/false conversion; strict/loose container inequality; boolean equality coercion; money multiplication → addition | Each relevant literal fails under its seed in each tier | All 19 cases pass per tier |
| V1_error_values_match_literal_parity_corpus and TS twin | Each money/date/null-number/nonfinite parameter key and value separately erased; fractional money coerced to integer | Each individual seed fails the named error case | All six error cases pass per tier |
| V1_numeric_money_accepts_an_exact_small_integer and TS twin | Reject numeric integer admission | Fails per tier | Passes per tier |
| V1_money_charges_each_operand_and_result_mantissa and TS twin | Remove initial, next-operand and result charge separately; add one initial step separately | Under-budget cases fail for removed charges; exact-budget cases fail for overcharge | All four boundaries pass per tier |
| Public_argument_guards_name_the_rejected_parameter | Remove first ExpandCollection null guard | Both-invalid assertion fails | Passes |
| pins the malformed binding diagnostic without a foreign iteration cause | Replace bindings guard with false; separately OR → AND | One failure per seed | Passes |

Every hand seed was restored byte-identically before its green run; final production matches platform main `62227059c3ec14fbe452cf8df7a00b182da16b76`. Direct scalar-string negation hit nullable compile error CS8602, which is not a red test; a compilable string-truthiness witness supplied the red proof, and the final JSON separately names the exact negation's killing test. No diagnostic suppression was used.

An initial scoped run reported three spurious kills because this round's test accidentally set .NET CancelAfter to zero. The test now retains the existing default liveness ceiling. Independent seeds at evaluator 177, 312 and 415 pass seeded and restored; the repeated final JSON reports them Survived, agreeing with their equivalence arguments. The discarded run supplies neither final scores nor killing evidence.


## Reports and test tree

- evaluator: `StrykerOutput/hlp.foundation.rule-runtime.tests-W4aG5K/reports/mutation-report.json`; SHA-256 `c72fcffe976cb53100dd22841d055a76872722af6beadab9b4a6244cb13fb8f2`.
- taxonomy: `StrykerOutput/hlp.foundation.taxonomy.tests-KSRzF3/reports/mutation-report.json`; SHA-256 `80a97bbfe100692f333a5ea0c4575301bf75f59f17d81f86fb31d39680073a48`.
- TypeScript: `projections/typescript/foundation/hlp.foundation.rule-runtime/reports/mutation/mutation.json`; SHA-256 `ffa8480e8d954eb789c0990e7f691ab35471ab0e09a2370d7419773c07be95c4`.
- Test source `projections/dotnet/foundation/hlp.foundation.rule-runtime.tests/CodingSubsumptionTests.cs`: SHA-256 `f3eb651e8c93d4f0653a58aaeb7367c48931319a7dee3307f1c18a0e686e2e58`.
- Test source `projections/dotnet/foundation/hlp.foundation.taxonomy.tests/TaxonomyInterpreterTests.cs`: SHA-256 `8b5b2a9c23cd9bea588966653d5ebc091c0bf415781df7860292fb7558567b8e`.
- Test source `projections/typescript/foundation/hlp.foundation.rule-runtime/src/__tests__/coding-subsumption.test.ts`: SHA-256 `eb61456dd09e43b00f45420e5fe6880af75312369ca72c429531a725ad4ced25`.
- Test source `projections/typescript/foundation/hlp.foundation.rule-runtime/src/__tests__/input-envelope-ijson.test.ts`: SHA-256 `3f18dd474bc2d2630a83886fa061de1c8f3556645d52e882fb5644a6d1b2d25c`.

## HarborlineJsonLogic.cs — every targeted mutant

| Source span | Mutator and replacement | Disposition | Killing test, equivalence proof or record gap |
|---|---|---|---|
| 48:47-48:56 | Linq method mutation (First() to FirstOrDefault()) → `obj.FirstOrDefault` | equivalent | The preceding object-count guard admits exactly one entry, so First and FirstOrDefault return the same entry. |
| 54:89-54:93 | String mutation → `""` | equivalent | RuleCompiler.ValidateOperators rejects unregistered executable keys before this internal dispatch; literal objects are returned before dispatch. |
| 66:12-66:58 | Conditional (true) mutation → `(true?Evaluate(args[i], ctx) :null)` | equivalent | Every Eval caller supplies an index within the arity admitted by BuiltInFunctionRegister; EvalVar separately guards its optional fallback. |
| 66:12-66:26 | Equality mutation → `i <= args.Count` | equivalent | Every Eval caller supplies an index within the arity admitted by BuiltInFunctionRegister; EvalVar separately guards its optional fallback. |
| 74:32-74:78 | Conditional (true) mutation → `(true?Evaluate(args[0], ctx) :null)` | equivalent | ScopeGrammar rewrites var to a nonempty literal path and admits at least one operand, so the empty-argument and null-path fallback arms are unreachable. |
| 74:32-74:46 | Equality mutation → `args.Count >= 0` | equivalent | ScopeGrammar rewrites var to a nonempty literal path and admits at least one operand, so the empty-argument and null-path fallback arms are unreachable. |
| 74:83-74:85 | String mutation → `"Stryker was here!"` | equivalent | ScopeGrammar rewrites var to a nonempty literal path and admits at least one operand, so the empty-argument and null-path fallback arms are unreachable. |
| 101:49-101:82 | Statement mutation → `;` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Missing_operators_preserve_pending_dependencies |
| 112:75-112:77 | String mutation → `"Stryker was here!"` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_agreed_operator_semantics_match_literal_parity_corpus |
| 118:49-118:82 | Statement mutation → `;` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Missing_operators_preserve_pending_dependencies |
| 119:47-119:132 | Statement mutation → `;` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Missing_some_preserves_the_bad_reference_identity |
| 119:75-119:130 | Null coalescing mutation (remove left) → `RuleError.Of(RuleEngineCodes.UpstreamError)` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Missing_some_preserves_the_bad_reference_identity |
| 177:13-177:22 | Equality mutation → `op != '+'` | equivalent | Unary minus has already returned; for every other one-operand registered arithmetic operator both paths apply ToNumber then NumNode to the same value. |
| 179:32-179:122 | Statement mutation → `;` | equivalent | The register and compiler require at least one operand for every arithmetic operator, so the zero-operand refusal is unreachable. |
| 179:100-179:104 | String mutation → `""` | equivalent | The register and compiler require at least one operand for every arithmetic operator, so the zero-operand refusal is unreachable. |
| 205:30-205:126 | Statement mutation → `;` | equivalent | The register and compiler require at least one operand for min and max, so every mutated zero-operand diagnostic is unreachable. |
| 205:98-205:102 | String mutation → `""` | equivalent | The register and compiler require at least one operand for min and max, so every mutated zero-operand diagnostic is unreachable. |
| 205:104-205:123 | Conditional (true) mutation → `(true?"min" :"max")` | equivalent | The register and compiler require at least one operand for min and max, so every mutated zero-operand diagnostic is unreachable. |
| 205:104-205:123 | Conditional (false) mutation → `(false?"min" :"max")` | equivalent | The register and compiler require at least one operand for min and max, so every mutated zero-operand diagnostic is unreachable. |
| 205:110-205:115 | String mutation → `""` | equivalent | The register and compiler require at least one operand for min and max, so every mutated zero-operand diagnostic is unreachable. |
| 205:118-205:123 | String mutation → `""` | equivalent | The register and compiler require at least one operand for min and max, so every mutated zero-operand diagnostic is unreachable. |
| 254:98-254:102 | String mutation → `""` | equivalent | ScopeGrammar admits exactly three nonempty static agg coordinates before this internal helper; BadAgg's null-coordinate diagnostics cannot run. |
| 254:104-254:109 | String mutation → `""` | equivalent | ScopeGrammar admits exactly three nonempty static agg coordinates before this internal helper; BadAgg's null-coordinate diagnostics cannot run. |
| 261:36-261:120 | Statement mutation → `;` | equivalent | The register admits at least one operand for every money operator, so the zero-operand refusal is unreachable. |
| 261:104-261:108 | String mutation → `""` | equivalent | The register admits at least one operand for every money operator, so the zero-operand refusal is unreachable. |
| 261:110-261:117 | String mutation → `""` | equivalent | The register admits at least one operand for every money operator, so the zero-operand refusal is unreachable. |
| 263:13-263:45 | Statement mutation → `;` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_money_charges_each_operand_and_result_mantissa |
| 267:17-267:47 | Statement mutation → `;` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_money_charges_each_operand_and_result_mantissa |
| 269:17-269:49 | Statement mutation → `;` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_money_charges_each_operand_and_result_mantissa |
| 275:81-275:85 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_error_values_match_literal_parity_corpus |
| 275:87-275:94 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_error_values_match_literal_parity_corpus |
| 284:17-284:47 | Negate expression → `!(v.TryGetValue<long>(out var l))` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Money_refuses_non_decimal_values_instead_of_coercing_them |
| 285:17-285:47 | Negate expression → `!(v.TryGetValue<int>(out var iv))` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Money_refuses_non_decimal_values_instead_of_coercing_them |
| 287:17-287:97 | Logical mutation → `v.TryGetValue<double>(out var d) && d == Math.Floor(d) \|\| Math.Abs(d) < 9.007e15` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Money_refuses_non_decimal_values_instead_of_coercing_them |
| 287:17-287:97 | Negate expression → `!(v.TryGetValue<double>(out var d) && d == Math.Floor(d) && Math.Abs(d) < 9.007e15)` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Money_refuses_non_decimal_values_instead_of_coercing_them |
| 287:17-287:71 | Logical mutation → `v.TryGetValue<double>(out var d) \|\| d == Math.Floor(d)` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Money_refuses_non_decimal_values_instead_of_coercing_them |
| 287:53-287:71 | Equality mutation → `d != Math.Floor(d)` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_error_values_match_literal_parity_corpus |
| 287:75-287:97 | Equality mutation → `Math.Abs(d) > 9.007e15` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_numeric_money_accepts_an_exact_small_integer |
| 287:75-287:97 | Equality mutation → `Math.Abs(d) <= 9.007e15` | contradicted | README Decision DI:61-65 and DES-0018 rules-ck-8:66 require parity, but native refuses an exact 9.007e15 integer that TS accepts; see the reproduced contradiction cases above. No defect-pinning test or runtime change. |
| 290:9-290:91 | Statement mutation → `;` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Money_refuses_non_decimal_values_instead_of_coercing_them |
| 290:35-290:89 | String mutation → `""` | equivalent | Only Money calls ToMoney; it catches FormatException and emits a fresh RuleError without propagating the FormatException message or cause. |
| 297:85-297:91 | String mutation → `""` | equivalent | DateAdd catches this FormatException and emits a fresh RuleError without propagating its message or cause. |
| 299:59-299:64 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_agreed_operator_semantics_match_literal_parity_corpus |
| 304:13-304:100 | Statement mutation → `;` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Date_operators_refuse_invalid_dates_instead_of_returning_a_value |
| 304:81-304:85 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_error_values_match_literal_parity_corpus |
| 304:87-304:97 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_error_values_match_literal_parity_corpus |
| 312:82-312:88 | String mutation → `""` | equivalent | DateDiff catches this FormatException and emits a fresh RuleError without propagating its message or cause. |
| 313:82-313:88 | String mutation → `""` | equivalent | DateDiff catches this FormatException and emits a fresh RuleError without propagating its message or cause. |
| 318:13-318:101 | Statement mutation → `;` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Date_operators_refuse_invalid_dates_instead_of_returning_a_value |
| 318:81-318:85 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_error_values_match_literal_parity_corpus |
| 318:87-318:98 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_error_values_match_literal_parity_corpus |
| 371:29-371:51 | Equality mutation → `i > escaped.Length - 1` | equivalent | RuntimeInputEnvelope rejects lone surrogates in all admitted data; CoreWorkDerivation.LiteralBytes reads authored string leaves before evaluation, so malformed literal strings fail before CodingMember. Other registered operators generate well-formed strings. This InvalidOperationException recovery loop is unreachable. |
| 371:29-371:51 | Equality mutation → `i <= escaped.Length - 1` | equivalent | RuntimeInputEnvelope rejects lone surrogates in all admitted data; CoreWorkDerivation.LiteralBytes reads authored string leaves before evaluation, so malformed literal strings fail before CodingMember. Other registered operators generate well-formed strings. This InvalidOperationException recovery loop is unreachable. |
| 371:33-371:51 | Arithmetic mutation → `escaped.Length + 1` | equivalent | RuntimeInputEnvelope rejects lone surrogates in all admitted data; CoreWorkDerivation.LiteralBytes reads authored string leaves before evaluation, so malformed literal strings fail before CodingMember. Other registered operators generate well-formed strings. This InvalidOperationException recovery loop is unreachable. |
| 371:53-371:56 | PostIncrementExpression to PostDecrementExpression mutation → `i--` | equivalent | RuntimeInputEnvelope rejects lone surrogates in all admitted data; CoreWorkDerivation.LiteralBytes reads authored string leaves before evaluation, so malformed literal strings fail before CodingMember. Other registered operators generate well-formed strings. This InvalidOperationException recovery loop is unreachable. |
| 371:58-371:65 | PostIncrementExpression to PostDecrementExpression mutation → `units--` | equivalent | RuntimeInputEnvelope rejects lone surrogates in all admitted data; CoreWorkDerivation.LiteralBytes reads authored string leaves before evaluation, so malformed literal strings fail before CodingMember. Other registered operators generate well-formed strings. This InvalidOperationException recovery loop is unreachable. |
| 372:21-372:39 | Equality mutation → `escaped[i] != '\\'` | equivalent | RuntimeInputEnvelope rejects lone surrogates in all admitted data; CoreWorkDerivation.LiteralBytes reads authored string leaves before evaluation, so malformed literal strings fail before CodingMember. Other registered operators generate well-formed strings. This InvalidOperationException recovery loop is unreachable. |
| 372:41-372:75 | AddAssignmentExpression to SubtractAssignmentExpression mutation → `i -= escaped[i + 1] == 'u' ? 5 : 1` | equivalent | RuntimeInputEnvelope rejects lone surrogates in all admitted data; CoreWorkDerivation.LiteralBytes reads authored string leaves before evaluation, so malformed literal strings fail before CodingMember. Other registered operators generate well-formed strings. This InvalidOperationException recovery loop is unreachable. |
| 372:46-372:75 | Conditional (true) mutation → `(true?5 :1)` | equivalent | RuntimeInputEnvelope rejects lone surrogates in all admitted data; CoreWorkDerivation.LiteralBytes reads authored string leaves before evaluation, so malformed literal strings fail before CodingMember. Other registered operators generate well-formed strings. This InvalidOperationException recovery loop is unreachable. |
| 372:46-372:75 | Conditional (false) mutation → `(false?5 :1)` | equivalent | RuntimeInputEnvelope rejects lone surrogates in all admitted data; CoreWorkDerivation.LiteralBytes reads authored string leaves before evaluation, so malformed literal strings fail before CodingMember. Other registered operators generate well-formed strings. This InvalidOperationException recovery loop is unreachable. |
| 372:46-372:67 | Equality mutation → `escaped[i + 1] != 'u'` | equivalent | RuntimeInputEnvelope rejects lone surrogates in all admitted data; CoreWorkDerivation.LiteralBytes reads authored string leaves before evaluation, so malformed literal strings fail before CodingMember. Other registered operators generate well-formed strings. This InvalidOperationException recovery loop is unreachable. |
| 372:54-372:59 | Arithmetic mutation → `i - 1` | equivalent | RuntimeInputEnvelope rejects lone surrogates in all admitted data; CoreWorkDerivation.LiteralBytes reads authored string leaves before evaluation, so malformed literal strings fail before CodingMember. Other registered operators generate well-formed strings. This InvalidOperationException recovery loop is unreachable. |
| 385:39-385:50 | Equality mutation → `o.Count >= 0` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_empty_object_is_falsy_in_every_logical_operator |
| 388:21-388:53 | Negate expression → `!(v.TryGetValue<string>(out var s))` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_agreed_operator_semantics_match_literal_parity_corpus |
| 388:62-388:74 | Equality mutation → `s.Length >= 0` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_agreed_operator_semantics_match_literal_parity_corpus |
| 389:21-389:44 | Negate expression → `!(TryDouble(v, out var d))` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_agreed_operator_semantics_match_literal_parity_corpus |
| 389:53-389:59 | Equality mutation → `d == 0` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_agreed_operator_semantics_match_literal_parity_corpus |
| 390:24-390:28 | Boolean mutation → `false` | equivalent | Owned JSON admits only null, arrays, objects, booleans, strings and numbers; every JsonValue kind returns before this fallback. |
| 391:29-391:33 | Boolean mutation → `false` | equivalent | Owned snapshots and compiler-produced values contain only the JSON node kinds already handled by the switch; no host JsonNode subclass crosses the evaluation boundary. |
| 399:92-399:100 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_error_values_match_literal_parity_corpus |
| 399:102-399:118 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_error_values_match_literal_parity_corpus |
| 402:60-402:69 | Conditional (true) mutation → `(true?1 :0)` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_agreed_operator_semantics_match_literal_parity_corpus |
| 402:60-402:69 | Conditional (false) mutation → `(false?1 :0)` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_agreed_operator_semantics_match_literal_parity_corpus |
| 406:85-406:93 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Numeric_operations_preserve_the_recorded_nonnumeric_refusal |
| 406:95-406:109 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Numeric_operations_preserve_the_recorded_nonnumeric_refusal |
| 415:72-415:76 | Boolean mutation → `false` | equivalent | All host values are serialized and reparsed by the owned snapshot boundary; authored JSON and registered operations yield JsonElement, int, long or double numbers, never a boxed decimal JsonValue. |
| 422:34-422:127 | Statement mutation → `;` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Nonfinite_arithmetic_fails_closed |
| 422:102-422:110 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_error_values_match_literal_parity_corpus |
| 422:112-422:124 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_error_values_match_literal_parity_corpus |
| 423:35-423:57 | Equality mutation → `Math.Abs(d) <= 9.007e15` | contradicted | README Decision DI:61-65 and DES-0018 rules-ck-8:66 require parity, but native refuses an exact 9.007e15 integer that TS accepts; see the reproduced contradiction cases above. No defect-pinning test or runtime change. |
| 432:56-432:76 | Conditional (true) mutation → `(true?"true" :"false")` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Legacy_coding_coercion_keeps_boolean_and_numeric_spellings |
| 432:56-432:76 | Conditional (false) mutation → `(false?"true" :"false")` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Legacy_coding_coercion_keeps_boolean_and_numeric_spellings |
| 432:60-432:66 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Legacy_coding_coercion_keeps_boolean_and_numeric_spellings |
| 432:69-432:76 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Legacy_coding_coercion_keeps_boolean_and_numeric_spellings |
| 433:17-433:47 | Negate expression → `!(v.TryGetValue<long>(out var l))` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Legacy_coding_coercion_keeps_boolean_and_numeric_spellings |
| 434:17-434:47 | Negate expression → `!(v.TryGetValue<int>(out var iv))` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Legacy_coding_coercion_keeps_boolean_and_numeric_spellings |
| 435:17-435:49 | Negate expression → `!(v.TryGetValue<double>(out var d))` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Legacy_coding_coercion_keeps_boolean_and_numeric_spellings |
| 442:13-442:35 | Logical mutation → `a is null && b is null` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 442:13-442:35 | Negate expression → `!(a is null \|\| b is null)` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 442:13-442:22 | Equality mutation → `a is not null` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 442:26-442:35 | Equality mutation → `b is not null` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 442:44-442:66 | Logical mutation → `a is null \|\| b is null` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 442:44-442:53 | Equality mutation → `a is not null` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 442:57-442:66 | Equality mutation → `b is not null` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 446:17-446:25 | Logical mutation → `an \|\| bn` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_equality_does_not_coerce_zero_string_to_number |
| 446:34-446:42 | Equality mutation → `ad != bd` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_equality_distinguishes_equal_and_unequal_numbers |
| 448:94-448:102 | Equality mutation → `ab != bb` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 449:17-449:75 | Equality mutation → `va.TryGetValue<bool>(out _) == vb.TryGetValue<bool>(out _)` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 449:84-449:89 | Boolean mutation → `true` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 452:16-452:52 | Equality mutation → `a.ToJsonString() != b.ToJsonString()` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_agreed_operator_semantics_match_literal_parity_corpus |
| 457:13-457:35 | Logical mutation → `a is null && b is null` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 457:44-457:66 | Logical mutation → `a is null \|\| b is null` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 457:44-457:53 | Equality mutation → `a is not null` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 457:57-457:66 | Equality mutation → `b is not null` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 464:17-464:35 | Logical mutation → `aCoerce \|\| bCoerce` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_loose_equality_coerces_numeric_strings_but_not_other_strings |
| 464:17-464:35 | Negate expression → `!(aCoerce && bCoerce)` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_loose_equality_coerces_numeric_strings_but_not_other_strings |
| 467:16-467:52 | Equality mutation → `a.ToJsonString() != b.ToJsonString()` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_agreed_operator_semantics_match_literal_parity_corpus |
| 472:41-472:45 | Boolean mutation → `false` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_loose_equality_coerces_numeric_strings_but_not_other_strings |
| 473:69-473:73 | Boolean mutation → `false` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.V1_agreed_operator_semantics_match_literal_parity_corpus |
| 474:13-474:76 | Negate expression → `!(v.TryGetValue<string>(out var s) && JsNumber.TryParse(s, out d))` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_loose_equality_coerces_numeric_strings_but_not_other_strings |
| 474:85-474:89 | Boolean mutation → `false` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_loose_equality_coerces_numeric_strings_but_not_other_strings |
| 476:16-476:21 | Boolean mutation → `true` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_loose_equality_coerces_numeric_strings_but_not_other_strings |


## TaxonomyInterpreter.cs — every targeted mutant

| Source span | Mutator and replacement | Disposition | Killing test, equivalence proof or record gap |
|---|---|---|---|
| 146:9-146:56 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 150:13-150:59 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 152:59-152:174 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Duplicate_pinned_sources_refuse_instead_of_selecting_the_first |
| 152:87-152:151 | String mutation → `$""` | ignored: non-contract text | Owner ruling: this exception prose is not read by a contract or test; exception type, Code and ParamName remain covered. Do not assert on its English wording (Taxonomy interface:58; DES-0024:248-258). |
| 272:59-272:64 | Boolean mutation → `true` | equivalent | UTF8Encoding's BOM flag affects GetPreamble, not either GetByteCount use of this instance. |
| 295:17-295:41 | Equality mutation → `Length + count >= 262_144` | equivalent | A body reaching exactly 262144 bytes is still refused by the final newline WriteByte, with the same capture-too-large identity; this private stream does not escape. |
| 295:17-295:31 | Arithmetic mutation → `Length - count` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Capture_stream_refuses_overflow_before_growing |
| 295:43-295:67 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Capture_stream_refuses_overflow_before_growing |
| 300:17-300:49 | Equality mutation → `Length + buffer.Length >= 262_144` | equivalent | A body reaching exactly 262144 bytes is still refused by the final newline WriteByte, with the same capture-too-large identity; this private stream does not escape. |
| 300:17-300:39 | Arithmetic mutation → `Length - buffer.Length` | equivalent | MemoryStream.Write(ReadOnlySpan<byte>) forwards derived streams to Stream.Write(span), which calls the overridden byte-array Write; its identical bound remains. Checked against dotnet/runtime v11.0.0 MemoryStream.cs. |
| 300:51-300:75 | Statement mutation → `;` | equivalent | MemoryStream.Write(ReadOnlySpan<byte>) forwards derived streams to Stream.Write(span), which calls the overridden byte-array Write; its identical bound remains. Checked against dotnet/runtime v11.0.0 MemoryStream.cs. |
| 313:9-313:56 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 320:9-320:55 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 320:56-320:101 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 321:103-321:171 | String mutation → `""` | ignored: non-contract text | Owner ruling: this exception prose is not read by a contract or test; exception type, Code and ParamName remain covered. Do not assert on its English wording (Taxonomy interface:58; DES-0024:248-258). |
| 330:9-330:55 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 337:9-337:49 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 344:9-344:49 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 347:59-347:111 | Collection initializer mutation → `new HashSet<string>(StringComparer.Ordinal) {}` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.A_descendant_cycle_at_the_depth_boundary_keeps_its_cycle_identity |
| 353:17-353:81 | Equality mutation → `depth > TaxonomyDefinitionAdmission.MaximumParentTraversalDepth` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Descendants_admit_64_edges_and_refuse_the_65th |
| 353:83-353:106 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Descendants_admit_64_edges_and_refuse_the_65th |
| 356:44-356:158 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.A_descendant_cycle_at_the_depth_boundary_keeps_its_cycle_identity |
| 356:81-356:104 | String mutation → `""` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.A_descendant_cycle_at_the_depth_boundary_keeps_its_cycle_identity |
| 356:106-356:156 | String mutation → `""` | ignored: non-contract text | Owner ruling: this exception prose is not read by a contract or test; exception type, Code and ParamName remain covered. Do not assert on its English wording (Taxonomy interface:58; DES-0024:248-258). |
| 357:54-357:63 | Arithmetic mutation → `depth - 1` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Descendants_admit_64_edges_and_refuse_the_65th |
| 365:9-365:57 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 365:58-365:108 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 375:9-375:49 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 394:9-394:27 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 405:9-405:55 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 405:56-405:106 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 406:9-406:27 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Expansion_refuses_malformed_inventories_and_collection_members |
| 407:27-407:55 | Null coalescing mutation (remove right) → `definition.Collections` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Expansion_refuses_malformed_inventories_and_collection_members |
| 409:43-409:160 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Expansion_refuses_malformed_inventories_and_collection_members |
| 409:80-409:111 | String mutation → `""` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Expansion_refuses_malformed_inventories_and_collection_members |
| 409:113-409:158 | String mutation → `""` | ignored: non-contract text | Owner ruling: this exception prose is not read by a contract or test; exception type, Code and ParamName remain covered. Do not assert on its English wording (Taxonomy interface:58; DES-0024:248-258). |
| 425:38-425:151 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Overlay_without_a_vendor_reference_keeps_its_refusal_identity |
| 425:73-425:109 | String mutation → `""` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Overlay_without_a_vendor_reference_keeps_its_refusal_identity |
| 425:111-425:149 | String mutation → `""` | ignored: non-contract text | Owner ruling: this exception prose is not read by a contract or test; exception type, Code and ParamName remain covered. Do not assert on its English wording (Taxonomy interface:58; DES-0024:248-258). |
| 432:88-432:90 | String mutation → `"Stryker was here!"` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Expands_overlay_refuses_by_name_when_the_vendor_version_is_not_installed |
| 440:143-440:193 | Logical mutation → `code is not null \|\| !vendorCodes.ContainsKey(code)` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Known_vendor_designations_do_not_block_overlay_expansion |
| 468:9-468:53 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 468:54-468:97 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 471:50-471:143 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Diff_refuses_mismatching_tenant_or_definition_identity |
| 471:78-471:141 | String mutation → `""` | ignored: non-contract text | Owner ruling: this exception prose is not read by a contract or test; exception type, Code and ParamName remain covered. Do not assert on its English wording (Taxonomy interface:58; DES-0024:248-258). |
| 489:41-489:101 | Collection initializer mutation → `new HashSet<string>(StringComparer.Ordinal) {}` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.A_descendant_cycle_at_the_depth_boundary_keeps_its_cycle_identity |
| 493:138-493:196 | String mutation → `$""` | ignored: non-contract text | Owner ruling: this exception prose is not read by a contract or test; exception type, Code and ParamName remain covered. Do not assert on its English wording (Taxonomy interface:58; DES-0024:248-258). |
| 494:103-494:153 | String mutation → `""` | ignored: non-contract text | Owner ruling: this exception prose is not read by a contract or test; exception type, Code and ParamName remain covered. Do not assert on its English wording (Taxonomy interface:58; DES-0024:248-258). |
| 500:9-500:55 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 500:86-500:198 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Expansion_refuses_malformed_inventories_and_collection_members |
| 500:123-500:154 | String mutation → `""` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Expansion_refuses_malformed_inventories_and_collection_members |
| 500:156-500:196 | String mutation → `""` | ignored: non-contract text | Owner ruling: this exception prose is not read by a contract or test; exception type, Code and ParamName remain covered. Do not assert on its English wording (Taxonomy interface:58; DES-0024:248-258). |
| 504:172-504:236 | String mutation → `""` | ignored: non-contract text | Owner ruling: this exception prose is not read by a contract or test; exception type, Code and ParamName remain covered. Do not assert on its English wording (Taxonomy interface:58; DES-0024:248-258). |
| 508:108-508:209 | String mutation → `$""` | ignored: non-contract text | Owner ruling: this exception prose is not read by a contract or test; exception type, Code and ParamName remain covered. Do not assert on its English wording (Taxonomy interface:58; DES-0024:248-258). |
| 512:80-512:122 | OrAssignmentExpression to ExclusiveOrAssignmentExpression mutation → `fields ^= TaxonomyNodeChangeFields.Display` | equivalent | Each distinct flag bit starts clear and is set once; OR and XOR produce the same bitset for all tracked-field combinations (taxonomy interface:59). |
| 513:88-513:134 | OrAssignmentExpression to ExclusiveOrAssignmentExpression mutation → `fields ^= TaxonomyNodeChangeFields.Description` | equivalent | Each distinct flag bit starts clear and is set once; OR and XOR produce the same bitset for all tracked-field combinations (taxonomy interface:59). |
| 514:48-514:89 | OrAssignmentExpression to ExclusiveOrAssignmentExpression mutation → `fields ^= TaxonomyNodeChangeFields.Status` | equivalent | Each distinct flag bit starts clear and is set once; OR and XOR produce the same bitset for all tracked-field combinations (taxonomy interface:59). |
| 515:86-515:131 | OrAssignmentExpression to ExclusiveOrAssignmentExpression mutation → `fields ^= TaxonomyNodeChangeFields.ParentCode` | equivalent | Each distinct flag bit starts clear and is set once; OR and XOR produce the same bitset for all tracked-field combinations (taxonomy interface:59). |
| 516:92-516:140 | OrAssignmentExpression to ExclusiveOrAssignmentExpression mutation → `fields ^= TaxonomyNodeChangeFields.SuccessorCode` | equivalent | Each distinct flag bit starts clear and is set once; OR and XOR produce the same bitset for all tracked-field combinations (taxonomy interface:59). |


## input-envelope.ts — every targeted mutant

| Source span | Mutator and replacement | Disposition | Killing test, equivalence proof or record gap |
|---|---|---|---|
| 63:49-63:51 | StringLiteral → `"Stryker was here!"` | equivalent | The fallback is unreachable: a valid object member has a following colon/delimiter within the string, and a root string is skipped by the no-object-frame guard before the whitespace scan. |
| 40:27-40:41 | ConditionalExpression → `true` | killed | I-JSON at the input envelope (T-1143) refuses two adjacent low surrogates rather than treating them as a pair |
| 40:45-40:65 | ConditionalExpression → `true` | equivalent | The unchanged comparisons against charCodeAt(i+1) reject an absent next code unit, so weakening only the preceding length test cannot admit a surrogate. |
| 40:45-40:65 | EqualityOperator → `i + 1 <= value.length` | equivalent | The unchanged comparisons against charCodeAt(i+1) reject an absent next code unit, so weakening only the preceding length test cannot admit a surrogate. |
| 40:45-40:50 | ArithmeticOperator → `i - 1` | equivalent | The unchanged comparisons against charCodeAt(i+1) reject an absent next code unit, so weakening only the preceding length test cannot admit a surrogate. |
| 38:19-38:35 | EqualityOperator → `i <= value.length` | equivalent | The single extra end iteration reads NaN from charCodeAt(length), matches no branch and changes no captured value or refusal. |
| 55:14-55:27 | ConditionalExpression → `false` | killed | I-JSON at the input envelope (T-1143) refuses a repeated member after nested containers, escapes or whitespace: {"key":0,"array":[],"key":1} |
| 55:29-55:44 | CallExpression → `;` | killed | I-JSON at the input envelope (T-1143) refuses a repeated member after nested containers, escapes or whitespace: {"key":0,"array":[],"key":1} |
| 56:14-56:44 | ConditionalExpression → `false` | killed | I-JSON at the input envelope (T-1143) refuses a repeated member after nested containers, escapes or whitespace: {"key":0,"array":[],"key":1} |
| 51:43-51:45 | ArrayDeclaration → `["Stryker was here"]` | equivalent | For valid parsed JSON every member-name lookup has its own object Set above the placeholder; a root scalar never has a following colon, so the extra bottom entry is never used. |
| 52:19-52:38 | EqualityOperator → `i <= jsonText.length` | equivalent | The single extra end iteration reads NaN from charCodeAt(length), matches no branch and changes no captured value or refusal. |
| 56:14-56:44 | LogicalOperator → `unit === 0x7d && unit === 0x5d` | killed | I-JSON at the input envelope (T-1143) refuses a repeated member after nested containers, escapes or whitespace: {"key":0,"array":[],"key":1} |
| 56:14-56:27 | ConditionalExpression → `false` | killed | I-JSON at the input envelope (T-1143) refuses a repeated member after nested containers, escapes or whitespace: {"key":0,"object":{},"key":1} |
| 56:46-56:56 | CallExpression → `;` | killed | I-JSON at the input envelope (T-1143) refuses a repeated member after nested containers, escapes or whitespace: {"key":0,"array":[],"key":1} |
| 59:59-59:90 | ConditionalExpression → `false` | killed | I-JSON at the input envelope (T-1143) refuses a repeated member after nested containers, escapes or whitespace: {"a\"b":0,"a\"b":1} |
| 63:14-63:52 | ConditionalExpression → `false` | killed | I-JSON at the input envelope (T-1143) refuses a repeated member after nested containers, escapes or whitespace: {"key" 	 :0,"key" 	 :1} |
| 63:31-63:51 | LogicalOperator → `jsonText[next] && ''` | killed | I-JSON at the input envelope (T-1143) refuses a repeated member after nested containers, escapes or whitespace: {"key" 	 :0,"key" 	 :1} |
| 63:54-63:60 | UpdateOperator → `next--` | killed | I-JSON at the input envelope (T-1143) refuses a repeated member after nested containers, escapes or whitespace: {"key" 	 :0,"key" 	 :1} |
| 56:31-56:44 | ConditionalExpression → `false` | killed | I-JSON at the input envelope (T-1143) refuses a repeated member after nested containers, escapes or whitespace: {"key":0,"array":[],"key":1} |
| 61:11-61:17 | ConditionalExpression → `false` | equivalent | JSON.parse has already validated syntax: strings outside an object frame cannot be followed by a member-name colon, so the later colon check skips them before any Set operation. |


## coding-subsumption.ts — every targeted mutant

| Source span | Mutator and replacement | Disposition | Killing test, equivalence proof or record gap |
|---|---|---|---|
| 15:48-15:92 | ConditionalExpression → `false` | killed | T-493 finite pinned coding reader refuses nontext capture arguments before reading caller properties |
| 15:9-15:92 | LogicalOperator → `typeof answersJsonText !== 'string' && typeof expectedBindingsJsonText !== 'string'` | killed | T-493 finite pinned coding reader refuses nontext capture arguments before reading caller properties |
| 15:9-15:92 | ConditionalExpression → `false` | killed | T-493 finite pinned coding reader refuses nontext capture arguments before reading caller properties |
| 15:9-15:44 | ConditionalExpression → `false` | killed | T-493 finite pinned coding reader refuses nontext capture arguments before reading caller properties |
| 42:36-42:50 | ConditionalExpression → `false` | equivalent | WeakMap.has already returns false for every unbranded value, including primitives and null; the unchanged final ownership check gives the same refusal without reading caller properties. |
| 42:7-42:50 | LogicalOperator → `typeof value !== 'object' && value === null` | equivalent | WeakMap.has already returns false for every unbranded value, including primitives and null; the unchanged final ownership check gives the same refusal without reading caller properties. |
| 42:7-42:50 | ConditionalExpression → `false` | equivalent | WeakMap.has already returns false for every unbranded value, including primitives and null; the unchanged final ownership check gives the same refusal without reading caller properties. |
| 42:7-42:32 | ConditionalExpression → `false` | equivalent | WeakMap.has already returns false for every unbranded value, including primitives and null; the unchanged final ownership check gives the same refusal without reading caller properties. |
| 54:7-54:12 | ConditionalExpression → `false` | equivalent | Every public evaluator/graph constructor first requires an owned snapshot; owned state is private and cannot be removed, so data is present at these internal lookups. |
| 65:7-65:12 | ConditionalExpression → `false` | equivalent | Every public evaluator/graph constructor first requires an owned snapshot; owned state is private and cannot be removed, so data is present at these internal lookups. |
| 82:32-82:54 | ConditionalExpression → `false` | killed | T-493 finite pinned coding reader refuses trailing members on every tuple |
| 82:7-82:54 | ConditionalExpression → `false` | killed | T-493 finite pinned coding reader refuses trailing members on every tuple |
| 82:7-82:54 | LogicalOperator → `!Array.isArray(value) && value.length !== count` | killed | T-493 finite pinned coding reader refuses trailing members on every tuple |
| 87:7-87:32 | ConditionalExpression → `false` | killed | T-493 finite pinned coding reader requires a string tenant even when both documents agree |
| 91:32-91:50 | ConditionalExpression → `false` | equivalent | An empty bindings array still fails: answers must be nonempty and every answer must name a present binding, so admission remains impossible with the same capture refusal. |
| 91:7-91:50 | ConditionalExpression → `false` | killed | T-493 finite pinned coding reader pins the malformed binding diagnostic without a foreign iteration cause |
| 91:7-91:50 | LogicalOperator → `!Array.isArray(value) && value.length === 0` | killed | T-493 finite pinned coding reader pins the malformed binding diagnostic without a foreign iteration cause |
| 108:9-108:35 | ConditionalExpression → `false` | killed | T-493 finite pinned coding reader compares an earlier tuple component before a later reversed component |
