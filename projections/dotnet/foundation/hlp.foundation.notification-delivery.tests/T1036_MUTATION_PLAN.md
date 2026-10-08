# T-1036 notification-delivery validation plan

## Candidate and ownership

Prepared from Platform main `b10d86e5045a2acd89e6522f04b95ccda788b1eb` and Control main `ecff5fe6b15030e23908c27f4d201e876317b19e`, fetched on 2026-10-07. T-1036 is still `ready` / `NeedsFix`; the open Platform PR search for notification-delivery returned no competing implementation. The original five test methods are present: four facts and one two-row theory.

Owned paths are `projections/dotnet/foundation/hlp.foundation.notification-delivery/`, this test directory, and Control's `tickets/T-1036-notification-delivery-stryker-baseline-is-42-percent/ticket.md`. The candidate changes tests and their audit material; production behavior is unchanged. It adds 23 test methods, giving 28 methods and 61 authored cases (21 facts, 19 InlineData rows and 21 MemberData rows). The hosted full-project run subsequently discovered and executed all 61 cases, as recorded below.

Reserve **only** this JSON key for the later measured ratchet:

```text
projections/dotnet/foundation/hlp.foundation.notification-delivery.tests/Harborline.Foundation.NotificationDelivery.Tests.csproj
```

At preparation, the `tooling/stryker-baselines.json` entry recorded score 42.37, break 42, 59 tested, 80 total, 25 detected, 34 undetected, measured 2026-09-30 at `8ee91fc61b305d39b12cfa41d3f879b3064ba2a4`. The preparation config had coverage-analysis off, JSON-only reporting and thresholds high 80 / low 60 / break 42. The measured ratchet below retains coverage and reporting policy while raising thresholds. Only this project's baseline entry and its config thresholds are reserved for the measured ratchet. Shared T-498 is owned by the controller; Control's isolated generated test references may be regenerated under the controller's authorization. The controller must integrate any shared dispatch/index change separately.

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
| InboxChannel 71 | No named kill; the originally proposed payload test does not distinguish it | `ConfigureAwait(false)` → `true`, continuation-context compatibility / 3; corrected after real report |

This table records the original intended test mapping, corrected for mutant71 after execution. Actual named killing tests and outcomes are in the measured report below; the supplemental hand replay did not run.

## Oracle and negative-control review

DES-0055 ck-1 supplies the literal `inbox`; ck-2 supplies tenant binding fields; ck-7/ck-10 and cc-2 supply per-person durable inbox state, full content and newest-first order. Refusal strings, required-field validation, null argument names and ascending-ID ties are explicit local API compatibility checks. The public `MarkAllReadAsync` XML contract specifies unread-only updates, supporting dismissed-state preservation. The design does not specify tie order or a MarkRead transition from Dismissed; the candidate makes no new design claim for either.

Expectations use literal payloads/codes, a fixed clock, literal ID sequences/counts, fixture input and before/after properties. They never sort production output to construct expected order or read production refusal constants as expected values. The persistence spy asserts that all seven null/empty/whitespace required fields fail before any append. The mixed fixture includes same-tenant/other-recipient, other-tenant/same-recipient, both different, case variants, read and dismissed entries. Successful and refused read operations assert complete entries, not just counts. Extra coverage pins input snapshots, duplicate-ID refusal, idempotence, commit waiting, committed return values, persistence failures and token forwarding.

An independent GPT-6.1 sol reviewer inspected the tests, historical survivor mapping and evidence plan on 2026-10-07. Its read-only test review found no blocking defect; the initial survivor mapping was provisionally accepted; actual execution later corrected mutant71, which remains an explicit counted continuation-context survivor. Review clarifications about local API requirements, dismissed-state preservation and tie-order provenance were applied. Driver review identified an incomplete provenance risk from untracked files: execution now rejects untracked owned files and any unstaged working-tree change, and records the candidate index tree plus SHA-256 of every tracked owned file. Read-only review does not establish compilation or execution.

The replay driver [run_negative_controls.py](run_negative_controls.py) is optional supplemental evidence. Its default mode is a lightweight read-only recipe check. It verifies the historical hash, exact source, all survivor mappings and named test presence. Execution requires a fully staged/committed candidate, then compiles/tests the clean candidate, replays each historical mutant against its named test, restores source in `finally`, then verifies the clean suite again. A compiler/tool failure or empty TRX never counts as a behavioral kill; a named target test must fail in TRX. Receipts/logs are written only to an explicitly supplied fresh output directory. Do not run `--execute` until the controller explicitly grants the heavy lane.

## Required publication and hosted evidence

The isolated candidate integrates Platform main `c0f66f451cd0feff5d7dbef67dbbcbed252d3121` and Control main `2ea70c17bcf63ca335e6331fab82964aab375fd6`. The original reviewed test code and independent oracles are unchanged. The notification config already has `coverage-analysis: off` for its MemberData theory; the pre-measurement thresholds were high 80 / low 60 / break 42.

[Platform CONTRIBUTING at the integrated authority](https://github.com/Harborline-Software/harborline-platform/blob/c0f66f451cd0feff5d7dbef67dbbcbed252d3121/CONTRIBUTING.md#before-you-push) requires fetching `origin/main` and running `node tooling/run-pr-preflight.mjs` before push. That preflight builds nothing and creates no gate receipt. The earlier local full-validation staging hold was controller-added and has been lifted. User authorization covers draft publication after the actual required cheap checks and independent review. No local .NET work or mini lease is authorized; API owners retain the native reservation.

The pre-existing hosted evidence executor is branch `evidence/platform-records-handmutants-20261007`, commit `7a3a251dddd3a6d8f5be2420943746483be55f11`, `.github/workflows/verify.yml` blob `4f2371acbfa9613dadccdd3a6c467f79b92be029`. Its full-project job checks out the exact uploaded `inputs.head`, records Platform commit/tree, Control identity and toolchain, invokes `node tooling/stryker.mjs full "$TEST_PROJECT"` and retains raw JSON plus complete logs. Dispatch with this candidate's exact uploaded SHA, the unique project fragment `hlp.foundation.notification-delivery.tests`, its Platform PR number and mode `full-project`. Verify the executor branch still resolves to the reviewed commit before dispatch. No new workflow or executor branch is needed.

Read the new report before ratcheting: require nonzero tested mutants, a score strictly above 42.37, candidate source/test/config binding, and all classified silent-scope/state/persistence survivors killed with named behavioral tests. Match the historical mutants by file/location/mutator/replacement, then resolve the fresh `killedBy` IDs through `testFiles`. Coverage-analysis off does not imply that named attribution is absent. Historical reports demonstrate report structure only and are not proof for this candidate. Any required mutant without real named proof remains an explicit gap. CompileError/Ignored are not test kills or owner-accepted waivers; classify every residual and report timeouts separately.

The 34 supplemental manual replays were proposed in this worker plan; T-1036 and the repository do not independently mandate those extra executions when the full-project raw report provides the same or stronger named mutant-to-test proof. The replay driver stays available for a genuine evidence gap. Do not run it locally without a new exclusive root grant. Do not waive any actual ticket or design requirement.

Keep only the reserved project's measured JSON value and config thresholds: `break = floor(score)`, `low = max(60, break)`, `high = max(80, break)`, with exact report counts/date/tested candidate commit. Preserve every other baseline value and mutation policy. A measured ratchet does not authorize invented results or a no-op test-only PR mutation run.

Normal readiness must trigger the actual hosted PR checks. Required verify and SBOM checks plus full protected merge-group native/gallery proof remain mandatory before protected landing. The separate evidence-only full-project dispatch does not replace them. Control retains its actual document gate, prescribed Node suite, hooks and independent review before its normal protected delivery. T-1036 acceptance stays open until named silent-survivor proof, the measured ratchet and real protected gate evidence are recorded.

### Local driver readiness retained as optional future proof

The task-local native driver reuses T-1012's repaired reviewed profile SHA `916f4d4f72a538bf2c58b5e4851a69724dfe913ade40669ad3a8b96b5bd71626`. Its own exact reviewed adaptation, source pins and synthetic checks are in the external handoff. It was never admitted: no local test, control, mutation or full gate ran and no lease was acquired. Its frozen pins predate this main integration; any future granted local use needs refreshed exact-candidate binding and review.

If a future genuine evidence gap requires the supplemental replays, the source entry point is `run_negative_controls.py --execute --output <fresh-directory>` under a separately granted lease. Its default mode checks recipes without compiling. The historical audit remains 34 survivors; the hosted report now contains61 discovered cases.

Duration calibration only: T-1012's actual native full Platform gate ran all 23 steps without reuse in 586.536628 seconds. A later separate local gate would merit a 15–20 minute planning slot, with normal resource admission. This does not measure T-1036, grant a slot or replace hosted qualification.

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
- **Stale checked-in Platform receipt:** `node tooling/validate-repository.mjs` exits 1 with `phase-4 gate shared-result count differs from fixtures`, identically on the candidate and a pristine worktree at base `b10d86e5`. The old receipt records 1224 shared results; current fixtures expect 1227 because data-exchange grew from two cases to five. The actual gate collects fresh results and compares them to current fixtures. This is stale evidence, not a demonstrated source/tooling defect; no count or gate code was hand-edited.

Hosted compiled discovery and named mutation proof are recorded below. Required current-head hosted checks and protected full native/gallery landing proof remain pending. No local heavy reservation is requested by this hosted route.

## Measured hosted result and ratchet

[Run37718895656](https://github.com/Harborline-Software/harborline-platform/actions/runs/37718895656) executed the full unique project on head `4eb8e0515b6ea7918ed24381fab212aa8999b023`, tree `28ab08195ffcc8fd576adb0b85f939821081d07a`, with Control `2ea70c17bcf63ca335e6331fab82964aab375fd6`. The existing executor used Stryker.NET5.0.0; its log records61 initial tests and34.9217181seconds. Raw [report](evidence/t1036-hosted-after.json) SHA256 `e9ef82216fb04be27817bf13da70f64983530e64095481218d4ff0cea5a38b93`; artifact11524881645 ZIP digest `786fe029af756ca21c755ad160a677ea44e6d333e488df650c1abfed356fee12` matches GitHub metadata.

80generated,59tested:58Killed,1Survived,16Ignored(block-already-covered filter),5CompileError,0Timeout/NoCoverage/RuntimeError. The repository wrapper truncates the score to98.30%; Stryker prints98.31% rounded. Compile errors remain5/80=6.25%, unchanged from baseline. Independent GPT-6.1-sol review verified raw hashes, both embedded production files, all four embedded test files and all34historical joins by file/location/mutator/replacement. The [summary](evidence/t1036-hosted-after-summary.json) lists the actual named `killedBy`/`testFiles` witnesses:33historical survivors are Killed, with all genuine scope/state/refusal/persistence risks covered. Four kills name a different valid behavioral witness than the initially planned method; actual report attribution is preserved.

The sole survivor71 changes `ConfigureAwait(false)` to `true` atInboxChannel.cs178. The initial audit mistakenly described it as an AnswerOwed mutation. DES-0055 sets no continuation-context requirement. This remains **Survived**, with no exclusion, ignore or waiver; no additional required proof gap was found. Payload true/false coverage remains valuable and executed, but is not claimed to kill this context mutant. The33named observed kills satisfy the actual ticket mutation requirement; the optional34hand replays were not executed.

The ratchet changes only the reserved notification baseline entry toscore98.30/break98, with58detected/1undetected and the actual tested head/date; high98/low98/break98 follow the repository formula. Every other baseline entry is unchanged. Protected native/gallery gate evidence is still required before T-1036 closes.
