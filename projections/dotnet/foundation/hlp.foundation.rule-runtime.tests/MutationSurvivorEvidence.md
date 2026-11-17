# T-493 answer-contract mutation dispositions

Production revision: `62227059c3ec14fbe452cf8df7a00b182da16b76` (current origin/main at final fetch). Every production file was restored byte-identically after hand seeding and matches that revision. Test source hashes below bind the measured test tree. Mutation runs used the repository's existing runners and configurations, with no full-project run or baseline/threshold change.

This inventory disposes every originally reported survivor/uncovered mutant in the four requested files. A design-call mark is an unresolved contract question, not equivalence or acceptance. Compile errors and timeouts are reported separately. Mutants are matched by file, source span, mutator and replacement, never by run-specific id.

## Scoped scores and counts

| File | Before this continuation | After | Killed | Timeout | Survived | No coverage | Equivalent | Needs design call | Compile error |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Native evaluator | 65.67% | 75.00% | 201 | 0 | 67 | 0 | 39 | 28 | 95 |
| Taxonomy interpreter | 72.43% | 89.72% | 192 | 0 | 22 | 0 | 10 | 12 | 203 |
| TS input envelope | 83.19% | 93.28% | 103 | 8 | 7 | 1 | 8 | 0 | 0 |
| TS reader | 91.93% | 95.96% | 214 | 0 | 9 | 0 | 7 | 2 | 0 |

Scores use detected / (detected + survived + uncovered), rounded to two decimals. The native wrapper truncates its summary at two decimals, so Taxonomy's wrapper summary is 89.71 while the report's rounded score is 89.72. Timeouts are not claimed as killing-test evidence. The TS package run had one pre-existing killed mutant become a timeout; all twelve targeted envelope mutants newly detected by the added cases are Killed in the final JSON.

The initial native reports had 112 evaluator and 59 Taxonomy survivors/uncovered mutants. Their dispositions are respectively 45 killed / 39 equivalent / 28 design calls, and 37 killed / 10 equivalent / 12 design calls. The targeted TS sets are 12 killed / 8 equivalent for the envelope and 9 killed / 7 equivalent / 2 design calls for the reader. No target lacks a disposition.

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

## Design calls

These are recorded against the cited source lines; none is treated as equivalent:

- **missing-some-key-coercion** (1; evaluator): DES-0018 rules-ck-7:65 describes canonical scope addresses but does not settle missing_some's null/nonstring dynamic-key conversion.
- **money-charge** (3; evaluator): DES-0018:151 and rules-ck-20:78 require bounded evaluation but do not state the per-operand mantissa charge quantum.
- **operator-error-params** (6; evaluator): DES-0018 rules-ck-10:68 and rules-ck-20:78 specify money semantics and refusal identities, not the exact money error parameter vocabulary.
- **money-integer-fence** (4; evaluator): DES-0018 rules-ck-10:68 specifies exact money but does not settle acceptance of numeric money operands at the 9.007e15 fence or fractional-double conversion.
- **null-date-unit** (1; evaluator): DES-0018 rules-ck-10:68 names deterministic date operators but does not settle a supplied null unit versus the day default.
- **scalar-truthiness** (4; evaluator): DES-0018 rules-ck-8:66 and the owner ruling settle empty objects and parity; the record does not state the literal truthiness of other string/scalar cases.
- **null-number-params** (2; evaluator): DES-0018 rules-ck-20:78 lists coded refusals but does not specify the null-to-number diagnostic parameter key/value.
- **boolean-number-coercion** (3; evaluator): DES-0018 rules-ck-8:66 closes the operator vocabulary and requires parity but does not specify boolean-to-number coercion literals.
- **nonfinite-params** (2; evaluator): DES-0018 rules-ck-20:78 requires fail-closed numeric errors but does not specify the nonfinite diagnostic parameter key/value.
- **container-equality** (2; evaluator): DES-0018 rules-ck-8:66 requires tier parity but does not state whether object/array equality is structural, ordered-serialization, or identity based.
- **taxonomy-diagnostic-prose** (11; taxonomy): DES-0024:248-258 and Taxonomy interface:58 preserve refusal identities and argument guards, not exact English exception-message prose; this message remains observable.
- **argument-precedence** (1; taxonomy): Taxonomy interface:58 preserves argument guards but does not specify which parameter is named when definition and collectionName are both null; removing this guard changes that precedence.
- **capture-shape-diagnostics** (2; reader): DES-0018:238-241 requires capture-style TypeError for malformed documents but does not settle its diagnostic message/cause for non-iterable binding values; removing the shape guard changes that observable detail.

Empty-object truthiness has no remaining design-call mark: the owner ruling is tested in both tiers.

## Reports and test tree

- evaluator: `StrykerOutput/hlp.foundation.rule-runtime.tests-ThPwHS/reports/mutation-report.json`; SHA-256 `ee16ee83dc4a5633daa1fb57c349449feacbb1978636cbd95507f97381ffb3d3`.
- taxonomy: `StrykerOutput/hlp.foundation.taxonomy.tests-ADr6UB/reports/mutation-report.json`; SHA-256 `393c6b9b90447c14ce35be203bc20bd11cc38c19bccbb31c4f47dc9b4e6af50d`.
- TypeScript: `projections/typescript/foundation/hlp.foundation.rule-runtime/reports/mutation/mutation.json`; SHA-256 `8786f41803bb7a063dd9ba8648d09f962f2b70431d53d738c80b777818c196a7`.
- Test source `projections/dotnet/foundation/hlp.foundation.rule-runtime.tests/CodingSubsumptionTests.cs`: SHA-256 `bcdce5e62fc6eb9a0253a276fe427c18226b87cc9fa75d66d421885281fe42aa`.
- Test source `projections/dotnet/foundation/hlp.foundation.taxonomy.tests/TaxonomyInterpreterTests.cs`: SHA-256 `ac3a34789188ee3de0254f2fc359c124e357a13938a263e41a55f9d5e7f8076d`.
- Test source `projections/typescript/foundation/hlp.foundation.rule-runtime/src/__tests__/coding-subsumption.test.ts`: SHA-256 `1c7707cd8e44236ed39bd45a13313c29fd480854ab56ca24c1c9428ed8533310`.
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
| 112:75-112:77 | String mutation → `"Stryker was here!"` | needs a design call: missing-some-key-coercion | DES-0018 rules-ck-7:65 describes canonical scope addresses but does not settle missing_some's null/nonstring dynamic-key conversion. |
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
| 263:13-263:45 | Statement mutation → `;` | needs a design call: money-charge | DES-0018:151 and rules-ck-20:78 require bounded evaluation but do not state the per-operand mantissa charge quantum. |
| 267:17-267:47 | Statement mutation → `;` | needs a design call: money-charge | DES-0018:151 and rules-ck-20:78 require bounded evaluation but do not state the per-operand mantissa charge quantum. |
| 269:17-269:49 | Statement mutation → `;` | needs a design call: money-charge | DES-0018:151 and rules-ck-20:78 require bounded evaluation but do not state the post-operation mantissa charge quantum. |
| 275:81-275:85 | String mutation → `""` | needs a design call: operator-error-params | DES-0018 rules-ck-10:68 and rules-ck-20:78 specify money semantics and refusal identities, not the exact money error parameter vocabulary. |
| 275:87-275:94 | String mutation → `""` | needs a design call: operator-error-params | DES-0018 rules-ck-10:68 and rules-ck-20:78 specify money semantics and refusal identities, not the exact money error parameter vocabulary. |
| 284:17-284:47 | Negate expression → `!(v.TryGetValue<long>(out var l))` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Money_refuses_non_decimal_values_instead_of_coercing_them |
| 285:17-285:47 | Negate expression → `!(v.TryGetValue<int>(out var iv))` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Money_refuses_non_decimal_values_instead_of_coercing_them |
| 287:17-287:97 | Logical mutation → `v.TryGetValue<double>(out var d) && d == Math.Floor(d) \|\| Math.Abs(d) < 9.007e15` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Money_refuses_non_decimal_values_instead_of_coercing_them |
| 287:17-287:97 | Negate expression → `!(v.TryGetValue<double>(out var d) && d == Math.Floor(d) && Math.Abs(d) < 9.007e15)` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Money_refuses_non_decimal_values_instead_of_coercing_them |
| 287:17-287:71 | Logical mutation → `v.TryGetValue<double>(out var d) \|\| d == Math.Floor(d)` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Money_refuses_non_decimal_values_instead_of_coercing_them |
| 287:53-287:71 | Equality mutation → `d != Math.Floor(d)` | needs a design call: money-integer-fence | DES-0018 rules-ck-10:68 specifies exact money but does not settle acceptance of numeric money operands at the 9.007e15 fence or fractional-double conversion. |
| 287:75-287:97 | Equality mutation → `Math.Abs(d) > 9.007e15` | needs a design call: money-integer-fence | DES-0018 rules-ck-10:68 specifies exact money but does not settle acceptance of numeric money operands at the 9.007e15 fence or fractional-double conversion. |
| 287:75-287:97 | Equality mutation → `Math.Abs(d) <= 9.007e15` | needs a design call: money-integer-fence | DES-0018 rules-ck-10:68 specifies exact money but does not settle acceptance of numeric money operands at the 9.007e15 fence or fractional-double conversion. |
| 290:9-290:91 | Statement mutation → `;` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Money_refuses_non_decimal_values_instead_of_coercing_them |
| 290:35-290:89 | String mutation → `""` | equivalent | Only Money calls ToMoney; it catches FormatException and emits a fresh RuleError without propagating the FormatException message or cause. |
| 297:85-297:91 | String mutation → `""` | equivalent | DateAdd catches this FormatException and emits a fresh RuleError without propagating its message or cause. |
| 299:59-299:64 | String mutation → `""` | needs a design call: null-date-unit | DES-0018 rules-ck-10:68 names deterministic date operators but does not settle a supplied null unit versus the day default. |
| 304:13-304:100 | Statement mutation → `;` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Date_operators_refuse_invalid_dates_instead_of_returning_a_value |
| 304:81-304:85 | String mutation → `""` | needs a design call: operator-error-params | DES-0018 rules-ck-10:68 and rules-ck-20:78 do not settle the exact date.add error parameter vocabulary. |
| 304:87-304:97 | String mutation → `""` | needs a design call: operator-error-params | DES-0018 rules-ck-10:68 and rules-ck-20:78 do not settle the exact date.add error parameter vocabulary. |
| 312:82-312:88 | String mutation → `""` | equivalent | DateDiff catches this FormatException and emits a fresh RuleError without propagating its message or cause. |
| 313:82-313:88 | String mutation → `""` | equivalent | DateDiff catches this FormatException and emits a fresh RuleError without propagating its message or cause. |
| 318:13-318:101 | Statement mutation → `;` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Date_operators_refuse_invalid_dates_instead_of_returning_a_value |
| 318:81-318:85 | String mutation → `""` | needs a design call: operator-error-params | DES-0018 rules-ck-10:68 and rules-ck-20:78 do not settle the exact date.diff error parameter vocabulary. |
| 318:87-318:98 | String mutation → `""` | needs a design call: operator-error-params | DES-0018 rules-ck-10:68 and rules-ck-20:78 do not settle the exact date.diff error parameter vocabulary. |
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
| 388:21-388:53 | Negate expression → `!(v.TryGetValue<string>(out var s))` | needs a design call: scalar-truthiness | DES-0018 rules-ck-8:66 and the owner ruling settle empty objects and parity; the record does not state the literal truthiness of other string/scalar cases. |
| 388:62-388:74 | Equality mutation → `s.Length >= 0` | needs a design call: scalar-truthiness | DES-0018 rules-ck-8:66 and the owner ruling settle empty objects and parity; the record does not state the literal truthiness of other string/scalar cases. |
| 389:21-389:44 | Negate expression → `!(TryDouble(v, out var d))` | needs a design call: scalar-truthiness | DES-0018 rules-ck-8:66 and the owner ruling settle empty objects and parity; the record does not state the literal truthiness of other numeric/scalar cases. |
| 389:53-389:59 | Equality mutation → `d == 0` | needs a design call: scalar-truthiness | DES-0018 rules-ck-8:66 and the owner ruling settle empty objects and parity; the record does not state the literal truthiness of other numeric/scalar cases. |
| 390:24-390:28 | Boolean mutation → `false` | equivalent | Owned JSON admits only null, arrays, objects, booleans, strings and numbers; every JsonValue kind returns before this fallback. |
| 391:29-391:33 | Boolean mutation → `false` | equivalent | Owned snapshots and compiler-produced values contain only the JSON node kinds already handled by the switch; no host JsonNode subclass crosses the evaluation boundary. |
| 399:92-399:100 | String mutation → `""` | needs a design call: null-number-params | DES-0018 rules-ck-20:78 lists coded refusals but does not specify the null-to-number diagnostic parameter key/value. |
| 399:102-399:118 | String mutation → `""` | needs a design call: null-number-params | DES-0018 rules-ck-20:78 lists coded refusals but does not specify the null-to-number diagnostic parameter key/value. |
| 402:60-402:69 | Conditional (true) mutation → `(true?1 :0)` | needs a design call: boolean-number-coercion | DES-0018 rules-ck-8:66 closes the operator vocabulary and requires parity but does not specify boolean-to-number coercion literals. |
| 402:60-402:69 | Conditional (false) mutation → `(false?1 :0)` | needs a design call: boolean-number-coercion | DES-0018 rules-ck-8:66 closes the operator vocabulary and requires parity but does not specify boolean-to-number coercion literals. |
| 406:85-406:93 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Numeric_operations_preserve_the_recorded_nonnumeric_refusal |
| 406:95-406:109 | String mutation → `""` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Numeric_operations_preserve_the_recorded_nonnumeric_refusal |
| 415:72-415:76 | Boolean mutation → `false` | equivalent | All host values are serialized and reparsed by the owned snapshot boundary; authored JSON and registered operations yield JsonElement, int, long or double numbers, never a boxed decimal JsonValue. |
| 422:34-422:127 | Statement mutation → `;` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Nonfinite_arithmetic_fails_closed |
| 422:102-422:110 | String mutation → `""` | needs a design call: nonfinite-params | DES-0018 rules-ck-20:78 requires fail-closed numeric errors but does not specify the nonfinite diagnostic parameter key/value. |
| 422:112-422:124 | String mutation → `""` | needs a design call: nonfinite-params | DES-0018 rules-ck-20:78 requires fail-closed numeric errors but does not specify the nonfinite diagnostic parameter key/value. |
| 423:35-423:57 | Equality mutation → `Math.Abs(d) <= 9.007e15` | needs a design call: money-integer-fence | DES-0018 rules-ck-10:68 does not settle the numeric storage/acceptance boundary feeding a computed value into money at 9.007e15. |
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
| 452:16-452:52 | Equality mutation → `a.ToJsonString() != b.ToJsonString()` | needs a design call: container-equality | DES-0018 rules-ck-8:66 requires tier parity but does not state whether object/array equality is structural, ordered-serialization, or identity based. |
| 457:13-457:35 | Logical mutation → `a is null && b is null` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 457:44-457:66 | Logical mutation → `a is null \|\| b is null` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 457:44-457:53 | Equality mutation → `a is not null` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 457:57-457:66 | Equality mutation → `b is not null` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_strict_and_loose_equality_respect_null_and_boolean_values |
| 464:17-464:35 | Logical mutation → `aCoerce \|\| bCoerce` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_loose_equality_coerces_numeric_strings_but_not_other_strings |
| 464:17-464:35 | Negate expression → `!(aCoerce && bCoerce)` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_loose_equality_coerces_numeric_strings_but_not_other_strings |
| 467:16-467:52 | Equality mutation → `a.ToJsonString() != b.ToJsonString()` | needs a design call: container-equality | DES-0018 rules-ck-8:66 requires tier parity but does not state whether object/array equality is structural, ordered-serialization, or identity based. |
| 472:41-472:45 | Boolean mutation → `false` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_loose_equality_coerces_numeric_strings_but_not_other_strings |
| 473:69-473:73 | Boolean mutation → `false` | needs a design call: boolean-number-coercion | DES-0018 rules-ck-8:66 does not specify the boolean numeric coercion used by loose equality. |
| 474:13-474:76 | Negate expression → `!(v.TryGetValue<string>(out var s) && JsNumber.TryParse(s, out d))` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_loose_equality_coerces_numeric_strings_but_not_other_strings |
| 474:85-474:89 | Boolean mutation → `false` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_loose_equality_coerces_numeric_strings_but_not_other_strings |
| 476:16-476:21 | Boolean mutation → `true` | killed | Harborline.Foundation.RuleEngine.Tests.CodingSubsumptionTests.Flat_loose_equality_coerces_numeric_strings_but_not_other_strings |


## TaxonomyInterpreter.cs — every targeted mutant

| Source span | Mutator and replacement | Disposition | Killing test, equivalence proof or record gap |
|---|---|---|---|
| 146:9-146:56 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 150:13-150:59 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 152:59-152:174 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Duplicate_pinned_sources_refuse_instead_of_selecting_the_first |
| 152:87-152:151 | String mutation → `$""` | needs a design call: taxonomy-diagnostic-prose | DES-0024:248-258 and Taxonomy interface:58 preserve refusal identities and argument guards, not exact English exception-message prose; this message remains observable. |
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
| 321:103-321:171 | String mutation → `""` | needs a design call: taxonomy-diagnostic-prose | DES-0024:248-258 and Taxonomy interface:58 preserve refusal identities and argument guards, not exact English exception-message prose; this message remains observable. |
| 330:9-330:55 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 337:9-337:49 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 344:9-344:49 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 347:59-347:111 | Collection initializer mutation → `new HashSet<string>(StringComparer.Ordinal) {}` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.A_descendant_cycle_at_the_depth_boundary_keeps_its_cycle_identity |
| 353:17-353:81 | Equality mutation → `depth > TaxonomyDefinitionAdmission.MaximumParentTraversalDepth` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Descendants_admit_64_edges_and_refuse_the_65th |
| 353:83-353:106 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Descendants_admit_64_edges_and_refuse_the_65th |
| 356:44-356:158 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.A_descendant_cycle_at_the_depth_boundary_keeps_its_cycle_identity |
| 356:81-356:104 | String mutation → `""` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.A_descendant_cycle_at_the_depth_boundary_keeps_its_cycle_identity |
| 356:106-356:156 | String mutation → `""` | needs a design call: taxonomy-diagnostic-prose | DES-0024:248-258 and Taxonomy interface:58 preserve refusal identities and argument guards, not exact English exception-message prose; this message remains observable. |
| 357:54-357:63 | Arithmetic mutation → `depth - 1` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Descendants_admit_64_edges_and_refuse_the_65th |
| 365:9-365:57 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 365:58-365:108 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 375:9-375:49 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 394:9-394:27 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 405:9-405:55 | Statement mutation → `;` | needs a design call: argument-precedence | Taxonomy interface:58 preserves argument guards but does not specify which parameter is named when definition and collectionName are both null; removing this guard changes that precedence. |
| 405:56-405:106 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 406:9-406:27 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Expansion_refuses_malformed_inventories_and_collection_members |
| 407:27-407:55 | Null coalescing mutation (remove right) → `definition.Collections` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Expansion_refuses_malformed_inventories_and_collection_members |
| 409:43-409:160 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Expansion_refuses_malformed_inventories_and_collection_members |
| 409:80-409:111 | String mutation → `""` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Expansion_refuses_malformed_inventories_and_collection_members |
| 409:113-409:158 | String mutation → `""` | needs a design call: taxonomy-diagnostic-prose | DES-0024:248-258 and Taxonomy interface:58 preserve refusal identities and argument guards, not exact English exception-message prose; this message remains observable. |
| 425:38-425:151 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Overlay_without_a_vendor_reference_keeps_its_refusal_identity |
| 425:73-425:109 | String mutation → `""` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Overlay_without_a_vendor_reference_keeps_its_refusal_identity |
| 425:111-425:149 | String mutation → `""` | needs a design call: taxonomy-diagnostic-prose | DES-0024:248-258 and Taxonomy interface:58 preserve refusal identities and argument guards, not exact English exception-message prose; this message remains observable. |
| 432:88-432:90 | String mutation → `"Stryker was here!"` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Expands_overlay_refuses_by_name_when_the_vendor_version_is_not_installed |
| 440:143-440:193 | Logical mutation → `code is not null \|\| !vendorCodes.ContainsKey(code)` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Known_vendor_designations_do_not_block_overlay_expansion |
| 468:9-468:53 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 468:54-468:97 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 471:50-471:143 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Diff_refuses_mismatching_tenant_or_definition_identity |
| 471:78-471:141 | String mutation → `""` | needs a design call: taxonomy-diagnostic-prose | DES-0024:248-258 and Taxonomy interface:58 preserve refusal identities and argument guards, not exact English exception-message prose; this message remains observable. |
| 489:41-489:101 | Collection initializer mutation → `new HashSet<string>(StringComparer.Ordinal) {}` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.A_descendant_cycle_at_the_depth_boundary_keeps_its_cycle_identity |
| 493:138-493:196 | String mutation → `$""` | needs a design call: taxonomy-diagnostic-prose | DES-0024:248-258 and Taxonomy interface:58 preserve refusal identities and argument guards, not exact English exception-message prose; this message remains observable. |
| 494:103-494:153 | String mutation → `""` | needs a design call: taxonomy-diagnostic-prose | DES-0024:248-258 and Taxonomy interface:58 preserve refusal identities and argument guards, not exact English exception-message prose; this message remains observable. |
| 500:9-500:55 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Public_argument_guards_name_the_rejected_parameter |
| 500:86-500:198 | Statement mutation → `;` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Expansion_refuses_malformed_inventories_and_collection_members |
| 500:123-500:154 | String mutation → `""` | killed | Harborline.Foundation.Taxonomy.Tests.TaxonomyInterpreterTests.Expansion_refuses_malformed_inventories_and_collection_members |
| 500:156-500:196 | String mutation → `""` | needs a design call: taxonomy-diagnostic-prose | DES-0024:248-258 and Taxonomy interface:58 preserve refusal identities and argument guards, not exact English exception-message prose; this message remains observable. |
| 504:172-504:236 | String mutation → `""` | needs a design call: taxonomy-diagnostic-prose | DES-0024:248-258 and Taxonomy interface:58 preserve refusal identities and argument guards, not exact English exception-message prose; this message remains observable. |
| 508:108-508:209 | String mutation → `$""` | needs a design call: taxonomy-diagnostic-prose | DES-0024:248-258 and Taxonomy interface:58 preserve refusal identities and argument guards, not exact English exception-message prose; this message remains observable. |
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
| 91:7-91:50 | ConditionalExpression → `false` | needs a design call: capture-shape-diagnostics | DES-0018:238-241 requires capture-style TypeError for malformed documents but does not settle its diagnostic message/cause for non-iterable binding values; removing the shape guard changes that observable detail. |
| 91:7-91:50 | LogicalOperator → `!Array.isArray(value) && value.length === 0` | needs a design call: capture-shape-diagnostics | DES-0018:238-241 requires capture-style TypeError for malformed documents but does not settle its diagnostic message/cause for non-iterable binding values; removing the shape guard changes that observable detail. |
| 108:9-108:35 | ConditionalExpression → `false` | killed | T-493 finite pinned coding reader compares an earlier tuple component before a later reversed component |
