# Kernel project test coverage, 2026-09-27

This run tested Platform source commit `a081ad8e` with .NET SDK `11.0.100-rc.1.26425.128` and Stryker.NET `5.0.0`. The coverage-enabled native runner passed 5,783 tests, including Core 48/48, Schema Validation 51/51 and Work Items 23/23. Its coverage settings exclude generated code and scope each Kernel result to its implementing project. All three summaries mapped every included source path; each suite produced one Cobertura report, so the branch totals are unambiguous.

| Project | Lines | Branches | Full Stryker score | Recorded break | Mutants tested |
|---|---:|---:|---:|---:|---:|
| Core | 185/186 | 51/60 | 68.64% | 68 | 116 |
| Schema Validation | 353/380 | 208/272 | 55.8% | 48 | 282 |
| Work Items | 480/521 | 111/174 | 43.64% | 42 | 241 |

The red-first `SchemaValidationTests.RequiredErrorOnlyNamesMissingFieldWhenNameContainsClosingBracket` exposed an incorrect extra required-field error. The fix parses the JSON array in the validator detail instead of stopping at the first `]`. Full Stryker mutant 335 in `InMemorySchemaRegistry.cs` was `Killed` by that test alone (test ID `cef1c4dc-06b7-25f6-6f80-10aeab37a57d`). `WorkItemKernelTests.FileJournal_RestartRetainsIdempotencyReceiptAndOutbox` proves replay after reopening the file journal, with one event, audit entry and outbox message. Full Stryker mutants 193, 197 and 199 in `InMemoryWorkItemStore.cs` were `Killed` by that test (test ID `44d55f58-a300-9685-d902-483ede879e7c`); the test was the sole killer for 197 and 199. Core's existing `KernelTransactionBoundaryTests.InvalidOperationIdentityIsRejectedBeforeTransactionBegins` still kills operation-validation mutant 125. No Core production or test behavior changed in this work.

Reproduce coverage in PowerShell from the Platform checkout:

```powershell
$env:HARBORLINE_GATE_COVERAGE = '1'
$env:HARBORLINE_CONTROL_REPO = 'C:\Projects\Harborline\harborline-control'
node tooling/run-native.mjs
```

Run each full mutation check with `node tooling/stryker.mjs full hlp.kernel.core.tests`, `node tooling/stryker.mjs full hlp.kernel.schema-validation.tests` and `node tooling/stryker.mjs full hlp.kernel.work-items.tests`. The source-scoped summaries are in `artifacts/quality/coverage/<suite>/coverage-summary.json`; raw Cobertura reports are in each summary's `artifactPath`. Full mutation JSON is in `StrykerOutput/<test-project>/reports/mutation-report.json`.

The raw reports remain local audit artifacts. Attach them to the owning delivery run with the source commit and tool versions before treating this as a release certificate. Full mutation reports still include survivors and Stryker compile errors, and T-909 still requires production caller proof for parts of Core.
