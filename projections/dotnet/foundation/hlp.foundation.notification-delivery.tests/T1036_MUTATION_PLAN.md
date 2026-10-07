# T-1036 notification-delivery validation plan

## Candidate and ownership

Prepared from Platform main `b10d86e5045a2acd89e6522f04b95ccda788b1eb` and Control main `ecff5fe6b15030e23908c27f4d201e876317b19e`, fetched on 2026-10-07. T-1036 is still `ready` / `NeedsFix`; the open Platform PR search for notification-delivery returned no competing implementation. The original five test methods are present: four facts and one two-row theory.

Owned paths are `projections/dotnet/foundation/hlp.foundation.notification-delivery/`, this test directory, and Control's `tickets/T-1036-notification-delivery-stryker-baseline-is-42-percent/ticket.md`. The candidate changes tests and their audit material; production behavior is unchanged. It adds 23 test methods, giving 28 methods and 61 authored cases (21 facts, 19 InlineData rows and 21 MemberData rows). Those counts are a source inventory, pending real discovery and execution.

Reserve **only** this JSON key for the later measured ratchet:

```text
projections/dotnet/foundation/hlp.foundation.notification-delivery.tests/Harborline.Foundation.NotificationDelivery.Tests.csproj
```

The current `tooling/stryker-baselines.json` entry remains score 42.37, break 42, 59 tested, 80 total, 25 detected, 34 undetected, measured 2026-09-30 at `8ee91fc61b305d39b12cfa41d3f879b3064ba2a4`. The existing config remains coverage-analysis off, JSON-only reporting and thresholds high 80 / low 60 / break 42. No other project's baseline or shared T-498/generated index is owned here. The controller must integrate any shared dispatch/index change separately.

## Observed surviving mutants

The raw historical report is [evidence/t1036-historical-before.json](evidence/t1036-historical-before.json), recovered from [Platform Stryker run 36874901580](https://github.com/Harborline-Software/harborline-platform/actions/runs/36874901580), artifact `11205339011`, named `stryker-report-36874901580`, on main `e679734cfa84a81cce5f3bcdae6263dfe6294d8a`. This is a historical full-run observation, **not a new candidate measurement or a claim that the entire workflow passed**.

- Archive SHA-256 `ec47cf49a65f2c887bf5c6b321372fb8f702795e75727dc5d8e367aef7c9de07` matches GitHub's artifact digest.
- Extracted report SHA-256 `6f780be2191cfd95df50155ea882a63386e21568d98c38b47ddea4d6f17c4dfd`.
- Both embedded production sources match current main after CRLF/LF normalization. The report names the same five original test methods, including both theory rows.
- 80 mutants: 25 Killed, 34 Survived, 16 Ignored by the tool's block-already-covered filter, 5 CompileError. No NoCoverage or Timeout. Tested denominator 59; score `floor(10000 * 25 / 59) / 100 = 42.37`.

[evidence/t1036-negative-control-plan.json](evidence/t1036-negative-control-plan.json) lists **every observed survivor** by file, line, mutator, historical ID, intended distinguishing test and independent oracle. IDs are only coordinates in this saved report; fresh Stryker IDs must be matched by file/location/mutator/replacement.

Audit priority 1 is silent scope disclosure/state corruption and fail-closed persistence/configuration; priority 2 is availability, cancellation, state projection and newest-first presentation; priority 3 is operator diagnostics and tie-order compatibility. This is a risk ranking for T-1036. DES-0055 has no `risk: silent` markings; it does not change an ADR-0103 verification state.

| Historical survivors | Intended distinguishing method (class in JSON plan) | Boundary / priority |
|---|---|---|
| DeliveryChannels 0, 1, 2 | `Notification_delivery_ck_1_inbox_key_renders_its_stable_name_and_default_renders_empty` | Literal inbox identity and default key rendering / 2 |
| DeliveryChannels 4 | `Notification_delivery_ck_2_null_reference_collection_is_refused_before_binding` | Null configuration refusal / 1 |
| DeliveryChannels 5 | `Notification_delivery_ck_2_empty_binding_preserves_tenant_channel_enablement_and_sender` | Legitimate secret-free binding / 2 |
| DeliveryChannels 13 | `Notification_delivery_ck_2_inbox_secrets_refuse_even_when_the_binding_is_disabled` | Operator explanation / 3 |
| DeliveryChannels 17 | `Notification_delivery_ck_1_unknown_channel_refuses_with_a_code_and_operator_explanation` | Operator explanation / 3 |
| InboxChannel 19 | `Notification_delivery_ck_10_append_rejects_null_and_duplicate_ids_without_losing_the_first_entry` | Persistence integrity / 1 |
| InboxChannel 20, 24, 32, 41, 53 | `Inbox_cancelled_operation_throws_without_changing_any_inbox` | Each of the five cancelled store operations / 2 |
| InboxChannel 26 | `Inbox_equal_timestamp_entries_use_ascending_id_as_a_repeatable_tiebreak` | Local compatibility order / 3 |
| InboxChannel 27 | `Notification_delivery_cc_2_entries_are_newest_first_even_when_appended_out_of_order` | DES-0055 newest-first order / 2 |
| InboxChannel 28 | `Notification_delivery_cc_2_list_isolates_both_tenant_and_recipient` | Silent disclosure / 1 |
| InboxChannel 36 | `Notification_delivery_cc_2_unread_count_excludes_read_dismissed_and_other_inboxes` | Silent scope/count corruption / 1 |
| InboxChannel 43, 45, 50, 51 | `Notification_delivery_ck_7_wrong_scope_or_missing_entry_refuses_without_changing_any_inbox` | Unauthorized state mutation / 1; diagnostic 51 / 3 |
| InboxChannel 55–60 | `Notification_delivery_ck_7_mark_all_read_changes_only_scoped_unread_entries_and_is_idempotent` | Cross-scope bulk mutation and unread-only state / 1 |
| InboxChannel 63 | `Notification_delivery_ck_10_null_request_refuses_before_any_append` | Null request refusal / 1 |
| InboxChannel 66, 67, 69, 70, 79 | `Notification_delivery_ck_10_missing_required_content_refuses_before_any_append` | Invalid content persistence / 1; diagnostic 79 / 3 |
| InboxChannel 71 | `Notification_delivery_ck_10_write_preserves_all_content_and_stamps_a_new_unread_entry` | False answer-owed fixture / 1 |

These are planned killing tests. No newly authored test has executed, and no new kill or improved score is claimed.

## Oracle and negative-control review

DES-0055 ck-1 supplies the literal `inbox`; ck-2 supplies tenant binding fields; ck-7/ck-10 and cc-2 supply per-person durable inbox state, full content and newest-first order. Refusal strings, required-field validation, null argument names and ascending-ID ties are explicit local API compatibility checks. The public `MarkAllReadAsync` XML contract specifies unread-only updates, supporting dismissed-state preservation. The design does not specify tie order or a MarkRead transition from Dismissed; the candidate makes no new design claim for either.

Expectations use literal payloads/codes, a fixed clock, literal ID sequences/counts, fixture input and before/after properties. They never sort production output to construct expected order or read production refusal constants as expected values. The persistence spy asserts that all seven null/empty/whitespace required fields fail before any append. The mixed fixture includes same-tenant/other-recipient, other-tenant/same-recipient, both different, case variants, read and dismissed entries. Successful and refused read operations assert complete entries, not just counts. Extra coverage pins input snapshots, duplicate-ID refusal, idempotence, commit waiting, committed return values, persistence failures and token forwarding.

An independent GPT-6.1 sol reviewer inspected the tests, historical survivor mapping and evidence plan on 2026-10-07. Its read-only test review found no blocking defect; all 34 survivors have a plausible distinguishing test. Review clarifications about local API requirements, dismissed-state preservation and tie-order provenance were applied. Driver review identified an incomplete provenance risk from untracked files: execution now rejects untracked owned files and any unstaged working-tree change, and records the candidate index tree plus SHA-256 of every tracked owned file. Read-only review does not establish compilation or execution.

The replay driver [run_negative_controls.py](run_negative_controls.py) is optional supplemental evidence. Its default mode is a lightweight read-only recipe check. It verifies the historical hash, exact source, all survivor mappings and named test presence. Execution requires a fully staged/committed candidate, then compiles/tests the clean candidate, replays each historical mutant against its named test, restores source in `finally`, then verifies the clean suite again. A compiler/tool failure or empty TRX never counts as a behavioral kill; a named target test must fail in TRX. Receipts/logs are written only to an explicitly supplied fresh output directory. Do not run `--execute` until the controller explicitly grants the heavy lane.

## Validation reservation and exact sequence

API worker `01a11830-869f-75db-b12f-e6533e9d90ce` currently holds the exclusive heavy local validation lease. No .NET build/test, negative-control execution, Stryker, full gate or CI dispatch has run for this candidate. Publication was authorized on 2026-10-07 after prescribed checks pass; that authorization does not grant the heavy lease.

After the controller grants a reservation, use the pinned toolchain from `global.json` and preserve the required gate's host applicability. From the Platform root:

```sh
dotnet test projections/dotnet/foundation/hlp.foundation.notification-delivery.tests/Harborline.Foundation.NotificationDelivery.Tests.csproj -c Debug --nologo
python3 projections/dotnet/foundation/hlp.foundation.notification-delivery.tests/run_negative_controls.py --execute --output /tmp/t1036-negative-controls-<fresh-run-id>
```

The first compiled run must confirm real discovery (61 authored cases are expected), all cases passing and compiler/analyzer outcomes. Replays should produce clean green → named behavioral red for each observed survivor → restored green, with logs and TRX retained. `--ids 28,36,43,45,55,56,57,58,59,60` allows a bounded first reservation for the scope/state class; a subset does not claim all 34.

Run Stryker natively in a **plain isolated clone** of the exact staged/committed candidate, as required by the wrapper's worktree warning. Record the tested candidate's commit/tree and patch hash; do not record the untouched base SHA as provenance for uncommitted test changes. Test-only changes are skipped by PR `run` mode, so explicitly select this project in `full`/`baseline` mode:

```sh
node tooling/stryker.mjs full hlp.foundation.notification-delivery.tests
node tooling/stryker.mjs baseline hlp.foundation.notification-delivery.tests
```

Inspect the JSON report before accepting the baseline: nonzero tested mutants, score strictly above 42.37, every priority-1 survivor killed with a named behavioral test/negative control, and no new unexplained silent survivor. With coverage-analysis off, a report may not attribute individual kills: use the replay's named-test TRX evidence rather than inventing attribution. Classify every residual by file/location/mutator; CompileError/Ignored are not test kills or owner-accepted waivers.

The `baseline` wrapper derives the measured entry and thresholds. Retain the ratchet only after inspecting evidence; never lower 42 or alter mutation exclusions. Integrate only the reserved project's JSON value and this test config's thresholds, checking all other baseline values are unchanged. Keep `break = floor(score)`, `low = max(60, break)`, `high = max(80, break)` and the report's exact counts/date/candidate commit. A whole-file reformat or another project's entry belongs to its owner.

The controller then runs the prescribed Platform phase-4 gate/receipt, Control gate and Control selftests with the existing coordination and required reviews, reconciles ticket evidence and combined generated views, and handles publication/hosted checks/protected merge. Never substitute the lightweight checks below for those gates. T-1036 stays open until the measured ratchet and real gate evidence meet its acceptance.

### Scheduling estimate (unmeasured on this candidate)

Request a **20-minute focused reservation**, separate from the full Platform gate: 3 minutes for cold focused restore/build/discovery, 10 minutes for all 34 named negative controls plus the driver's clean-before/clean-after runs, 4 minutes for selected-project full/baseline Stryker and 3 minutes to inspect reports and integrate the measured ratchet. This is a planning budget, not an observed duration or a timeout waiver. If infrastructure fails or the first run indicates the budget is insufficient, release/coordinate another reservation rather than overlap the API holder. A first 10-control scope/state batch can be scheduled separately but does not prove all 34 survivors.

Calibration: job `110411708081` in historical run `36874901580` recorded notification-delivery Stryker **28.3275683 seconds**, from analysis 2026-10-02T01:58:00.0840991Z through the report at 01:58:28.3975296Z, with six original discovered cases. That was a Windows run and does not measure the 61-case Mac mini candidate or the replay driver's repeated builds.

Budget the **full Platform phase-4 gate separately at 60 minutes**, after focused validation. The checked-in historical gate receipt's 23 step durations sum to 41.17325 minutes, including shared conformance 13.82 minutes, gallery 9.87, consumers 6.53 and native tests 5.11. This older receipt is duration calibration only; it is not current-head proof, does not identify this candidate's runtime and cannot guarantee warm dependency feeds or quiet-host availability.

## Completed lightweight checks

- Raw artifact digest/report digest and embedded production-source identity checked.
- Source inventory: original five methods verified; 23 additional methods authored.
- `python3 .../run_negative_controls.py`: all 34 recipes and named methods validated; no .NET launched.
- Python driver parsed successfully using `ast.parse` without executing validation.
- `node tooling/stryker.mjs check hlp.foundation.notification-delivery.tests`: PASS, 38 configured/excluded projects inspected; no mutation launched.
- `git diff --check`: PASS.
- Control `node tools/tickets/ticket-views.mjs`: 0 blocking, 7 existing advisories; no generated view writes.
- Follow-up isolated Control regeneration, authorized by the controller: only `TEST-REFERENCES.md` and `milestones/M11-every-primitive-authored-and-bound/TESTS.md` changed, with T-1036 test-file references explicitly marked execution state not recorded. Ticket, milestone, folder and connection generators ran; source-related outputs are rebased/regenerated from the combined tree later.
- Focused Control Node/document tests: 21 passed, 0 failed, approximately 2.59 seconds (`document-register`, `ticket-views-status`, `ticket-views-milestones`, `view-root-isolation`).
- Control's complete fast pre-commit checks passed after adding workspace-local sibling symlinks. The named source checkouts were read-only: API origin/main `d112b6e2fd5d0fc5295026ef17d18ba55dba9e68`, App origin/main `50cfa56b9d1d88e43309ea6729993d9eb2b1dc87`, Platform origin/main `b10d86e5045a2acd89e6522f04b95ccda788b1eb`. No primary source files or refs changed. Initial MEMBER-LINE topology staleness disappeared; no MEMBER-LINE edit was needed.
- Platform's fast pre-commit scanners passed when the existing hook was invoked through `sh`. Its tracked hook mode is 100644; local freezing uses an executable task-local wrapper that delegates to the unchanged repository hook.
- **Existing Platform source-validator blocker:** `node tooling/validate-repository.mjs` exits 1 with `phase-4 gate shared-result count differs from fixtures`, identically on the candidate and a pristine worktree at base `b10d86e5`. The recorded receipt/fixture disagreement is left to the full-gate controller; no unrelated receipt or gate code was edited.

Compiled discovery, behavioral test results, negative-control outcomes, measured ratchet, full gates, push/PR and merge evidence remain pending reservation and execution.
