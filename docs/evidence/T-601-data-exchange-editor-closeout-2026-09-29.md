# T-601 Data exchange editor closeout, 2026-09-29

This closes the DES-0023 Data exchange editor acceptance items on T-601, based on origin/main `b6f311b` (T-600 is done):

- ADR 0096 lane conformance for `DataExchangeAuthoringEditor` (React) and `HarborlineDataExchangeAuthoringEditor` (Blazor). It covers `readOnly`, both catalogue option shapes, fallback sections, and explicit pending-edit handling when the identity or revision changes.
- Both lanes render identical server census and staleness values, and neither lane can edit run evidence.
- `data-exchange-eng-22`, `data-exchange-auth-9` and `data-exchange-run-4`.

## What each lane now does

| Behaviour | React | Blazor |
| --- | --- | --- |
| Read-only admission | `readOnly` prop disables every input, select, checkbox and button. `set` and each intent handler return early. | `ReadOnly` parameter, the same disabling. `Change` and the `Discover`/`Save`/`DryRun`/`Commit`/`Publish` wrappers return early. |
| Pending edits | Draft carries `identity` and `expectedRevision`. A changed key is adopted silently unless there are local edits whose content differs. Otherwise a `role="alert"` "Revision changed." offers Keep edits and Discard edits. Every intent is withheld while the alert stands. Discard emits the incoming draft through `onChange`. | Same, with `Identity` and `ExpectedRevision` init properties and `OnParametersSet`. A same-key re-render leaves the pending state alone. |
| Option shapes | A catalogue entry is `{id, label}` or a bare id (`DataExchangeCatalogueOption`). | `DataExchangeOption` converts implicitly from a bare id. |
| Fallback sections | "No source columns discovered.", "No mappings authored." and a "Persisted run evidence" fieldset reading "No dry run recorded." | Same markup. |
| Run evidence | Rendered from the server summary verbatim. The section holds no control. | Same. |

The gallery scenarios always supply columns, mappings and a run, and a `false` `disabled` renders no attribute in either lane. The two gallery surfaces therefore render the same markup as before.

## Shared fixture

`conformance/hlp.ui.data-exchange/fixtures.yaml` (fixture revision 2) gains `data-exchange.read-only`, `data-exchange.pending-revision` and `data-exchange.run-evidence`. `data-exchange.stale-review` now carries a census and its expected staleness and census text. `interface.yaml` lists the same case order (interface revision 2, catalog updated). The React lane test (`src/__tests__/DataExchangeAuthoringEditor.lane.test.tsx`) and the bUnit suite (`DataExchangeAuthoringEditorTests.cs`) both read the file. `Shared_fixture_conforms` also asserts staleness and census per case.

`node conformance/hlp.ui.data-exchange/runners/run-shared.mjs` exited 0. The React suite and all five Blazor cases passed.

## Red first

- React: the new lane file ran 6 failed and 4 passed. The first failure was `readOnly_admission_disables_every_input_and_publish`: `<input aria-label="Definition name">` was not disabled. The four passes were the census/staleness theory for `run-evidence` and `stale-review`, the editable-admission case and the silent-adoption case, all behaviour that already existed.
- Blazor, step 1: the build failed. `ReadOnly`, `Identity` and `ExpectedRevision` did not exist, and there was no string-to-`DataExchangeOption` conversion.
- Blazor, step 2: I added only the API surface, with no behaviour. The run gave 5 failed and 14 passed. The failures were the readOnly admission test, the pending-edit test, the discard test, the fallback sections test and the census theory for the read-only case.

## Green

- React: `npm run test:data-exchange` passed 29 tests in 2 files. `tsc -p tsconfig.build.json` and `npm run typecheck` were clean.
- Blazor: the `DataExchangeAuthoringEditorTests` filter passed 35 of 35. The whole `Harborline.UIAdapters.Blazor.Tests` project passed 797 of 799. The two failures were `DataGridPerformanceTests` wall-clock budgets, which pass 4 of 4 when run alone, both on this branch and on origin/main. The data grid is untouched here.
- `validate-repository.mjs --allow-stale-gate` reported no errors; that flag is how the gate calls it. Strict `pnpm validate` reports "phase-4 gate shared-result count differs from fixtures", because the three new cases change the expected count. That receipt is recorded only from a complete run at a commit main holds, so it is refreshed after landing.
- `sync-ui-spec-authority --check`, `scan-prop-vocabulary`, `eng/verify-boundaries.sh`, `stryker.mjs check` and `strykerjs.mjs check` all passed. `run-ui-gate-model` shows the same two FAIL rows as origin/main. Its receipt now records 5 conformance cases for this module.

## Manual mutations

Each mutation was applied to one lane, the named test was run alone, and the source was restored. All 12 were killed. Both lanes were re-run at the final source.

| Id | Ticket | Mutation | React killer | Blazor killer |
| --- | --- | --- | --- | --- |
| M1 | auth-9 | Definition name input ignores readOnly | `readOnly_admission_disables_every_input_and_publish` | `ReadOnly_admission_disables_every_input_and_publish` |
| M2 | auth-9 | Publish enablement drops the readOnly/pending gate | same | same |
| M3 | eng-22 | A revision change overwrites pending edits (`revisionChanged` forced false) | `pending edits survive a revision change…` | `Pending_edits_survive_a_revision_change_until_the_author_keeps_or_discards_them` |
| M4 | eng-22 | A pending revision no longer withholds intents | same | same |
| M5 | run-4 | Staleness label inverted | census/staleness theory (3 of 3 cases red) | `Renders_the_server_census_and_staleness_verbatim…` (3 of 3 red) |
| M6 | run-4 | Census `conflicted` and `rejected` swapped | census/staleness theory (`run-evidence` and `stale-review` red) | same (the all-zero read-only case cannot see it) |

## Stryker

`tooling/stryker.mjs` read MSBuild's `IntermediateOutputPath` as `obj\Debug/net10.0/`, and the backslash made the Razor run fail on Linux with `ENOENT`. The PR workflow runs on ubuntu-latest, so this change normalises the separator.

Stryker.NET 5.0.0, PR mode (`node tooling/stryker.mjs run hlp.ui.button.tests`), mutated the changed data-exchange sources. It tested 132 mutants: 126 detected and 7 undetected, a score of 94.73. The first run scored 48.12; the tests added since then close the gaps it found. Killing tests for the new logic, matched by `.razor` line:

- `IntentsEnabled`, `CommitEnabled` and `PublishEnabled` (lines 30-32): the census theory and `ReadOnly_admission_disables_every_input_and_publish`.
- `OnParametersSet` (lines 36-40): `A_host_echo_of_the_same_revision_keeps_the_edit_pending_for_the_next_revision_change` and `A_saved_revision_that_echoes_the_local_content_is_adopted_without_an_alert`.
- `DiscardEdits` (line 47): `Discarding_a_pending_edit_adopts_the_incoming_server_revision`.
- The `ReadOnly` guard in `Change` (line 49): the host-echo test.
- The five intent wrappers (lines 55-59): `true` is killed by `ReadOnly_admission_disables_every_input_and_publish`, `false` by `An_editable_admission_keeps_the_authoring_controls_enabled_and_emits_each_intent`.
- The `DataExchangeAuthoringTypes.cs` initialisers: `The_empty_draft_is_blank_apart_from_its_csv_and_append_defaults`. It builds a fresh instance, because the cached static `Empty` is created before a mutant activates.

The 7 undetected mutants:

- Equivalent: the two `AuthoredContent` blanking strings (line 45), which replace both sides of the comparison with the same constant, and three `sequence++` to `sequence--` in the options render fragment (lines 67-69), which change only diff hints.
- Pre-existing, left alone: the form's `onsubmit` attribute name and the null fallback in `Text()` (line 48).

StrykerJS 10.0.0, PR mode (`node tooling/strykerjs.mjs run projections/react/ui/hlp.ui.data-exchange`): 207 killed, 4 survived and 2 runtime errors on the changed lines. The first run had 38 undetected, which led to the field, mapping, identity-only, saved-echo, not-Ready and optional-intent tests. I also deleted React's `host === loaded.current` guard, because the effect runs only when `host` changes. The 4 survivors:

- Equivalent: the two `?? ''` fallbacks in `revisionKey`, which are the same constant on both sides, and `if (!incoming) return` in `discardEdits`, because Discard exists only while `incoming` is set.
- Coverage artefact: `readOnly = false` becoming `true`. Applied by hand, it fails 21 of 29 tests. `perTest` coverage misattributes the default-parameter mutant.

Raw reports, trimmed to the data-exchange files:

- [.NET](mutation/t601-dotnet-data-exchange-editor-2026-09-29.json), SHA-256 `D091850644CD41C5856EDCC1D6ACA64C82221C366513EC09D7570DE20B47CD21`
- [TS](mutation/t601-ts-data-exchange-editor-2026-09-29.json), SHA-256 `45653FB53424143AA8DE41E81FD0988552F6F676B6FC49B8450A8A708F855B5C`

These are PR-mode runs, and no baseline was changed.

## Not covered here

- The ADR 0096 text is not in this repository. "Both option shapes" and "fallback sections" are implemented as described above, following the pending-edit pattern `HarborlineRulesAuthoringEditor` already ships ("Revision changed." with Keep edits and Discard edits).
- This worktree cannot produce the committed phase-4 receipt and the gallery run. That happens in the merge group, where the gallery shards run.
