# T-740 part 1: Rules editor authority and payload refusals, 2026-09-29

Both Rules editors now take an optional authority: the host's Access verdicts over the `RulesPermissions` capability names (DES-0018 §6). React takes `authority: { granted }` and Blazor takes `Authority: RulesEditorAuthority(Granted)`. The editor asks and never decides. When no authority is passed, the editor behaves as before. When one is passed, each lifecycle control is enabled only if its capability is granted:

- Save draft and Archive need `rules:author`.
- Publish needs `rules:publish`.
- Preview needs `rules:evaluate-explain` and `records:read`.

Without `rules:author`, the editor is read-only. The form carries `data-read-only`, and every authoring control is inside a disabled `fieldset`.

A producer response can now carry `refusals`, the `DefinitionRefusal` code/pointer list. The editor accepts it under the existing request fence and renders it as given, one `<li data-code data-pointer>code at pointer</li>` per refusal. The list clears on the next edit, on a clean external revision, or on the next accepted response.

Both lanes read one shared fixture: `conformance/hlp.blocks.builder-definitions/rules-editor-authority-fixtures.json` (`denyAll`, `threeRefusals`).

## Red first

The tests were written before the implementation.

- React: `deny_all_authority_renders_zero_enabled_publish_controls` failed on `toHaveAttribute("data-read-only")`. `three_refusal_payload_renders_exactly_its_code_pointer_refusals` failed with no `list` named `Refusals`. Result: 2 failed, 15 passed.
- Blazor: the tests first failed to compile, with no `RulesEditorAuthority`. After the types and an unused `Authority` parameter were added, both tests failed on assertions: `data-read-only` was absent, and the refusal collection was `[]`. Result: 2 failed, 13 passed.

## Manual mutations

For each mutation, the implementation was edited, the lane's rule-authoring suite was run, and the file was restored.

| # | Lane | Mutation | Result | Killed by |
|---|------|----------|--------|-----------|
| R1 | React | `readOnly = !allowed('save-draft')` → `false` | 1 failed / 16 passed | deny_all_authority_renders_zero_enabled_publish_controls |
| R2 | React | Publish `disabled={!allowed('publish')}` removed | 1 failed / 16 passed | deny_all_authority_renders_zero_enabled_publish_controls |
| R4 | React | `setRefusals(response.refusals)` → first two only | 1 failed / 16 passed | three_refusal_payload_renders_exactly_its_code_pointer_refusals |
| R5 | React | Refusal text drops `at {pointer}` | 1 failed / 16 passed | three_refusal_payload_renders_exactly_its_code_pointer_refusals |
| R6 | React | `data-pointer` carries the code | 1 failed / 16 passed | three_refusal_payload_renders_exactly_its_code_pointer_refusals |
| B1 | Blazor | `ReadOnly => !Allowed("save-draft")` → `false` | 1 failed / 14 passed | deny_all_authority_renders_zero_enabled_publish_controls |
| B2 | Blazor | Publish `Enabled="@Allowed("publish")"` removed | 1 failed / 14 passed | deny_all_authority_renders_zero_enabled_publish_controls |
| B4 | Blazor | `_refusals = Response.Refusals` → `Take(2)` | 1 failed / 14 passed | three_refusal_payload_renders_exactly_its_code_pointer_refusals |
| B5 | Blazor | Refusal text drops `at @refusal.Pointer` | 1 failed / 14 passed | three_refusal_payload_renders_exactly_its_code_pointer_refusals |
| B6 | Blazor | `data-pointer` carries the code | 1 failed / 14 passed | three_refusal_payload_renders_exactly_its_code_pointer_refusals |

R3 and B3 removed an `if (!allowed(operation)) return` guard at the top of `request` / `Request`, and both survived. The only callers are the lifecycle buttons, and those are already disabled for an ungranted operation. The guard was redundant, so it was deleted rather than tested, following the T-772 precedent. The producer still refuses the operation (T-591, `RulesPermissionGateTests`).

## Review round 1 (CI `stryker`, CodeRabbit)

- **CI `stryker` failed before it mutated anything.** `tooling/stryker.mjs` `razorRun` joined MSBuild's `IntermediateOutputPath`, and on Linux that property reads `obj\Debug/net10.0/`, with the default `obj\` base. Node's POSIX `path.join` keeps the backslash as a literal character, so `readdirSync` hit ENOENT. This PR was the first to change a `.razor` file under the Linux job. The fix normalises the separators. Run locally, `node tooling/stryker.mjs run` then exited 0 with a JSON report. Blazor project in PR mode: 253 tested, 115 detected, score 38.33. No survivor was on the new editor lines.
- **That run left survivors in `RulesEditorAuthority`:** the `rules:publish` string, both `CanPreview` strings, and `&&` → `||`. The shared fixture gained `grantCases`, and both lanes gained `each_partial_grant_enables_exactly_its_lifecycle_controls`. Manual mutations B7 (`&&` → `||`), B8 (`"rules:publish"` → `""`) and B9 (`"records:read"` → `""`) each fail that Blazor test and pass nothing else. R7 (React preview needs only `rules:evaluate-explain`) fails it in React.
- **CodeRabbit (major): a host echo cleared the refusals in Blazor.** An accepted `Authoritative` or `Materialization` response reports `Current` through `ValueChanged`. When the host bound it back into `Value`, the revision check took the echo for a clean external revision and cleared the refusals and the outcome. The outcome half predates this PR. The fix advances `_revision` when the accepted response is applied, as React's `latest.current` already did. `host_echo_of_an_accepted_revision_keeps_the_response_refusals_and_outcome` failed red in Blazor (0 refusals, 3 expected) and passes now. The React copy of the test passed before and after, pinning parity.

## Verification

- React `npm run test:rule-authoring`: 17 passed. Full `npm run test:native` (every React UI module): exit 0.
- React `tsc -p tsconfig.build.json --noEmit` on `hlp.ui.rule-authoring`, after building the `hlp.ui.button` dist: clean. `hlp.ui.button` `npm run typecheck`: clean.
- Blazor `Harborline.UIAdapters.Blazor.Tests`, filtered to `RulesAuthoringTests`: 15 passed.
- Full Blazor project: 789 passed, 2 failed. Both failures were `DataGridPerformanceTests` 10k-row time budgets. Run alone, they passed 4/4 on this branch twice and 4/4 on `main`. The diff does not touch the data grid.
- `node tooling/validate-repository.mjs`: `errors: []`.

Trace states (T-740:27) are not part of this change. T-730 edits the same files later.
