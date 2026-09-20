# T-485 platform controls: reuse assessment and reviewed UI follow-up

Started: 2026-09-19. Updated: 2026-09-20. Status: implemented and visually reviewed; the full repository gate passed as recorded below. This document is a narrative, not a gate receipt. PR landing remains separate.

The producer landed independently in platform PR 77 at `6fcb809a`, excluding the UI renderer changes. This follow-up is a bounded T-485 slice: its ADR 0099 acceptance explicitly assigns `forms-eng-3`, `forms-bound-1` and `forms-bound-4` (control selection and value-domain resolution) to the platform UI producer. It does not reopen T-621, close T-485, claim authored-and-bound readiness, change the API's T-664 dispatch, or change the builder, pack or Data Exchange. The source preview worktree and its pending merge are preserved. This follow-up starts at the merged producer and carries only the reviewed UI implementation and its tests.

Chris requested replacement of SchemaForm's bespoke controls, then required missing controls to start with a spec and justify a new identity over extension. This assessment applies that instruction; it does not attribute a particular interface choice or visual approval to Chris.

## Existing capability and gap

| Existing module | Existing responsibility | Gap for SchemaForm | Decision |
| --- | --- | --- | --- |
| SelectField | Controlled option identity and single selection, listbox keyboard state, field metadata, popup, dismissal and focus | Editable query separate from selection; bounded matching; multiple controlled values | Extend the same owner. |
| Input | Controlled text-like input, attributes, appearance and native editing | No option list or selection state | Reuse inside searchable SelectField and for remaining visible native-input kinds. |
| SearchInput | Debounced search draft, immediate clear, busy state | Its interface explicitly excludes results and query execution; searchbox is not a selection combobox | Do not repurpose it or add a second results owner. |
| Spotlight | Modal command palette, ranked sections, query and action selection | Inline field focus and controlled value selection are different from modal commands | Do not use a command palette as a field editor. |
| Popover | Anchored nonmodal dialog, placement, dismissal, focus | No option selection, field metadata or editable-combobox contract | Not a replacement for a field control; avoid reproducing SelectField around it in SchemaForm. |
| CheckBox | One Boolean or mixed state with label and field attributes | No ordered multi-value field or listbox navigation | Retain Boolean fields; do not build another multi-value editor from checkboxes in SchemaForm. |
| Button | Action/submit semantics, disabled/loading state, appearance and interaction | No missing capability for form/collection actions | Reuse without a new control identity. |

Evidence: the current interface.yaml files under specs/modules/ui for the seven named modules; React SelectField.tsx, SchemaForm controls.tsx and SchemaForm.tsx; Blazor HarborlineSelectField.razor, HarborlineSchemaDomainControl.razor and HarborlineSchemaControl.razor. Both module and projection catalogs contain no separate ComboBox, AutoComplete or MultiSelect producer. A catalog name alone would not establish a usable implementation; the existing public interfaces were inspected as well.

## Decision and alternatives

Extend SelectField at its existing framework seam. Selection cardinality and editable search vary presentation and interaction over the same caller-owned ordered options; neither introduces a new domain owner. Preserve single selection as the default and make the additional modes explicit. Reuse Input for text entry. Keep Rules, membership resolution, authorization, remote queries and persistence outside the UI module.

Separate ComboBox and MultiSelect identities would reproduce option identity, labels, disabled options, listbox navigation, controlled selection, dismissal and popup geometry. This scope supplies no distinct responsibility that pays for those additional interfaces. Wrapping Input/SearchInput, Popover and CheckBox inside SchemaForm would keep the duplicated behavior in the consumer, so it is also rejected. No new component is admitted by this decision.

The cost of extension is a larger mode matrix. The revision-2 SelectField contract therefore specifies plain single, searchable single, multiple and searchable multiple behavior, controlled updates, empty matches, disabled options, stale membership, keyboard/IME handling and popup reflow. Existing single callers must remain source-compatible. A future feature with a genuinely different ownership or interaction model must repeat this evaluation rather than accumulating unrelated flags.

Accessibility constrains the modes without requiring separate module identities. WAI's [combobox pattern](https://www.w3.org/WAI/ARIA/apg/patterns/combobox/) describes single selection; its [listbox pattern](https://www.w3.org/WAI/ARIA/apg/patterns/listbox/) supports multiple selection. The multiple mode therefore uses a button trigger and focusable multi-select listbox, not a multi-value combobox. Optional filtering uses a separate searchbox beside the listbox. The single searchable mode remains an editable combobox. The initial draft's all-mode combobox wording was corrected during specification review, before production implementation began.

## Spec before implementation

SelectField interface revision 2 is the authority for the additional modes. SchemaForm interface revision 4 records the composition rule and preservation requirements. These specifications precede source implementation. React expresses value/callback cardinality through a discriminated interface; Blazor keeps the existing single Value/ValueChanged pair and adds an explicit multiple Values/ValuesChanged pair. Query drafts never become candidates.

The implementation sequence is neutral fixtures and public interaction tests, shared control extension, consumer replacement, obsolete consumer behavior/style removal, native and package-consumer checks, then both galleries. SchemaForm retains structural HTML and hidden form data; it must not retain its own picker keyboard/listbox implementation behind platform-looking styling.

## Required evidence and review

- Public SelectField tests exercise each mode, exactly-once callbacks, controlled non-mutation, dismissal, IME, disabled/stale options, local matching and the 40,000-option/25-render bound.
- SchemaForm tests preserve domain membership, candidate/query separation, explicit choice, authority/read-only states, collection limits and submission/action behavior through the composed controls.
- Packaged React and Blazor gallery checks cover open menus, short and long labels, keyboard/pointer interaction, accessibility, RTL, themes and narrow/enlarged-text reflow. New capability examples belong to SelectField as well as its SchemaForm consumer.
- Fresh visual review is required for both changed surfaces. Previous SchemaForm approval is not reused, and implementation authorization is not visual approval.
- Only a subsequent successful full repository gate may claim a passing platform gate. The existing three-run authorization balance is unchanged by this specification work.

## Implemented composition and focused evidence

Both SchemaForm projections now use platform SelectField for domain search and multiple selection, Input for remaining visible input kinds, and Button for submit, collection and caller actions. Existing platform controls continue to own the other field kinds. The native-control source audit found only hidden inputs; structural HTML remains intentional. SchemaForm no longer owns query, listbox or picker keyboard state. No new component identity was created.

SelectField owns the shared narrow-container minimum-width fix. Its canonical stylesheet and both projection mirrors match. The redundant SchemaForm trigger-width override and both obsolete SelectField reflow exceptions were removed. Exact acknowledgment of a multiple toggle retains active navigation and search draft, including when the caller recreates semantically identical options; unrelated changes invalidate that state. The Blazor demonstration explicitly refreshes its parent-owned save status after its submission function returns a result.

Focused verification on 2026-09-19:

- React SelectField: 31 tests passed across three files after the final producer changes.
- `node tooling/verify-package-fixtures.mjs --only dynamic-forms-capability-vertical`: passed against fresh React and Blazor libraries. Ten corpus cases agreed across the two lanes; six submissions were accepted and four refused fail-closed. The packaged consumers had no source/project dependencies.
- Fresh gallery preparation, React and gallery-test typechecks, and the Blazor gallery build passed. NuGet audit remained enabled. The prepared Blazor library version was `0.0.0-alpha.0.h802d7e0e1069`.
- The 22 SelectField/SchemaForm gallery scenarios passed with no failures, skipped cases or retries producing flaky results. This includes domain selection/submission, open-popup accessibility and reflow at 320px with 200% text. The completed report started at `2026-09-19T22:39:45.420Z`.
- Native browser probes passed repeated Space toggles and Tab exit in all four framework/mode combinations. Both literal-choice popups kept all six labels unwrapped. Full-viewport screenshots were also inspected in both frameworks.

External evidence is retained under `C:/Users/Chris/AppData/Local/Temp/harborline-t621-8c81419867494b438ec1267151703908`: `t621-controls-prepared-4-select-native.log`, `t621-controls-prepared-4-consumers.log`, `t621-controls-prepared-host-4-*.log`, `t621-controls-gallery-third-1789857584649.json`, `t621-controls-browser-native-probe-4.log`, `t621-controls-browser-popup-probe-4.log`, and `t621-controls-popup-full-{react,blazor}.png`.

An earlier browser run was interrupted by an account usage limit and supplied no passing receipt; it is excluded. The completed run is focused evidence, not the platform gate. At the end of that 2026-09-19 verification, neither SelectField nor SchemaForm had received fresh owner visual approval for these changes. No full gate attempt or approval renewal was made during that control-composition work.

## Taxonomy scrolling review, 2026-09-20

Chris reported two taxonomy scrollbars in both frameworks and said, "select field looks good". That positive feedback applies to the reviewed appearance; SchemaForm was not approved. The scrolling correction below changes shared SelectField CSS, so no approval digest is renewed from the earlier feedback.

The original browser reproduction measured two vertical scroll owners in each framework: the popup had 300px of scroll content inside 286px, while the listbox had 324px inside 288px. Both had `overflow: auto`; the listbox height plus popup padding exceeded the outer cap. The permanent browser regression failed with `[null, "listbox"]` instead of `["listbox"]` in `t621-scroll-gallery-red-1789877324352.json`.

The SelectField contract now explicitly assigns vertical scrolling to the listbox. Its shared popup is a constrained flex column, the listbox may shrink within the available height, and the optional multiple-mode search input does not shrink or join the scrolling region. This is a correction at the existing owner, not a new taxonomy component or a SchemaForm styling workaround. The diagnostic override reduced both reproductions to one scrollbar before the source fix was applied.

Permanent gallery checks cover taxonomy and record search at wide/narrow viewports and 100%/200% text, actual list scrolling, and visibility and selection of the last match. Searchable multiple selection also checks that its separate search input remains visible and usable while the list scrolls. The build interrupted to resolve a cross-session lane race is excluded from evidence; no partial feed was reused.

Fresh verification passed against rebuilt libraries (`0.0.0-alpha.0.h9c1b3a933561` for Blazor): 31 SelectField tests, the dynamic-forms package-consumer fixture, preview typechecks/build, and all 22 gallery scenarios with zero failures, skips or flaky results. The green report is `t621-scroll-gallery-green-1789877984317.json`, started at `2026-09-20T04:19:44.839Z`. The original reproduction also passed without diagnostic style injection: each framework had one listbox scroll owner, 324px of content inside 274px, and no outer scrollbar. Its log is `t621-controls-scroll-green-probe.log`; full-viewport screenshots `t621-scroll-fixed-react.png` and `t621-scroll-fixed-blazor.png` were inspected. These files remain in the external evidence directory above.

Sandbox preview restore again hit the known NuGet TLS/signature endpoint error after library checks passed. The previously authorized host-only preview preparation completed with audit enabled; no warning or freshness check was disabled. The focused proof does not replace the platform gate.

## Owner feedback after the correction

Chris subsequently said, "the taxonomy popup looks right" in this task, after the corrected previews were presented. Together with the preceding "select field looks good", this records the visual review feedback for SelectField and its SchemaForm taxonomy consumer. It does not claim approval of the whole Forms primitive, API adoption, or unreviewed components. Before recording the two module verdicts, the declared SelectField and SchemaForm render surfaces in this follow-up were compared with the source preview worktree and were identical. The full repository gate must still pass on this follow-up tree.

Static preflight then found three missing visual-parity declarations for the new SelectField search/multiple scenarios. Their quality-case lists now enable the existing real cross-framework screenshot comparisons. This changes verification metadata only: no scenario content, rendered props, source component, style or interaction changes. No parity exclusion or new owner judgment is claimed. The SelectField review record binds this final catalog and identifies the metadata-only change explicitly.

## Follow-up gate repairs

The first follow-up gate found a fixed-count assertion in the expired-review backlog test. Removing SelectField's genuinely renewed review from that backlog changes its expected count from 51 to 50; the exact-set, deadline and verdict checks remain intact. The focused backlog suite then passed all five tests.

The second follow-up gate passed all 305 tooling tests and the build, then found two native-test integration faults: the downstream `use-form-rule-graph` test configuration lacked SchemaForm's new Button alias, and the React lane-purity check correctly rejected Blazor browser-adapter tests placed under the React projection. The alias is supplied. The four adapter cases now live in the neutral `tests/blazor-browser` harness, still exercise the shipped Blazor JavaScript, and remain mandatory and counted in the native runner. Reusable native evidence includes that directory in its input hash, with a closure regression test. No assertion, audit, architecture boundary or rendered surface was relaxed. Focused verification and the full follow-up gate remain required.

The subsequent workspace-write Codex lane passed the focused checks on 2026-09-20: nine tooling tests, four neutral Blazor adapter tests, all 1,471 React native tests, and all 25 architecture tests. The existing React script's serial-performance exclusions were unchanged. The three new SelectField browser scenarios (`search`, `multiple`, `multiple-search`) passed their real visual-parity, accessibility and reflow assertions against the reviewed previews, with one worker and zero retries. Their frozen dependency installation used pnpm 11.1.3 with scripts disabled and changed no lockfile. Actual logs and command/exit metadata are retained as `t485-focused-1-{tooling,neutral,react,architecture,gallery}.*` in the external evidence directory. This confirms the integration repairs; the full repository gate remains a separate requirement.

The final authorized host full-gate attempt ran from `2026-09-20T05:56:16.675Z` to `2026-09-20T06:13:38.754Z` against unchanged HEAD `6fcb809a504061f8f5f8fee0b4e288db6b740196` and index tree `c184128da3c52333a3815a2b134d9de2582c58d4`. Twenty steps passed, including 4,590 native tests, performance budgets, 1,220 shared conformance results and 14 clean package consumers. The gallery stage refused to start because the retained review preview occupied port 6106 (`EADDRINUSE`). It ran zero full-sweep browser tests, so the overall result is FAIL, not a passing gate. The complete report, exit metadata and preserved failure log are `t485-host-gate-3-report.json`, `t485-host-gate-3-result.json` and `t485-host-gate-3-gallery-full.log` in the external evidence directory. The gate already supports dedicated gallery ports through `HARBORLINE_GALLERY_REACT_PORT` and `HARBORLINE_GALLERY_BLAZOR_PORT`; using unused ports requires no source or check change and preserves the review previews. All five additional host full-gate runs authorized by Chris are now consumed. Another host full-gate run requires renewed authorization; no PR has been opened or merged.

Chris then answered "yes" to the explicit request for one more full gate on separate ports while preserving the review previews. That authorizes one additional host full-gate attempt, not an unrestricted retry loop. The run uses the existing supported port overrides for 6206 and 6207, keeps audit enabled, and follows the normal shared platform lock. Its result must be recorded separately; approval to run is not a passing result.

That run completed from `2026-09-20T14:31:37.782Z` to `2026-09-20T14:55:42.820Z`, with unchanged HEAD `6fcb809a504061f8f5f8fee0b4e288db6b740196` and index tree `d7b22e54bfb0f09e7e2955a0bb5311138f151dcf`. All twenty pre-gallery steps passed again. The gallery built and its 561 browser tests completed successfully (478 scenario tests and 83 other tests), with no flakes. The gate still returned FAIL: measured accessibility scans were 962, while the declaration expected 956. The full report and preserved gallery log are `t485-host-gate-4-report.json` and `t485-host-gate-4-gallery-full.log` in the external evidence directory. The isolated gallery servers exited, and the platform lane was handed to T-588 before diagnosis.

The three new SelectField interaction branches each scan an open popup in both projections, in addition to the baseline scan. The expectation omitted those six real checks. A small regression test first failed (`14 !== 20` on its seven-scenario fixture). The declaration now counts the extra scan only for those running scenarios; whole-scenario CI exclusions remove both scans, pixel exclusions remove neither, and capture mode retains the scans. Observed annotations and exact reconciliation remain unchanged. All 27 observation, shard-merge and input-closure tests then passed. Replaying the completed report against the corrected declaration reconciled every check family exactly, including 962 accessibility scans. This replay is diagnostic evidence, not a replacement full-gate PASS. No rendered source or owner-review surface changed. The one additionally authorized full run is consumed; no further full gate, PR or merge has been performed.

Chris subsequently said "approved" in response to the request for another full gate with the corrected count. This grants one additional host full-gate run on the isolated gallery ports, with the normal shared lock and audits enabled. No automatic retries or passing verdict are implied by this authorization.

## Passing full gate

`node tooling/run-phase-4-gate.mjs` completed with exit 0 and status PASS from `2026-09-20T15:31:04.368Z` to `2026-09-20T15:57:42.448Z`. HEAD remained `6fcb809a504061f8f5f8fee0b4e288db6b740196` and the tested index remained `aae0b3a6d30d483155f5a41f9d0f3a512eacfa66` throughout. All 22 required steps passed, including audited restore, build, 4,590 native tests, serial performance budgets, 1,220 shared conformance results, 14 clean package consumers, the full gallery and final catalog validation.

The gallery ran 561 browser tests: 478 scenario tests and 83 other tests. Its outcome was 561 expected, zero unexpected, zero flaky and zero skipped. It measured 962 accessibility scans, 114 visual-parity comparisons and 126 reflow checks; both scenario and check reconciliation were exact. Dedicated ports 6206/6207 preserved the review previews. The platform slot was released after natural completion.

The generated passing report is `docs/evidence/phase-4/gate.json`. The external full stdout and process/unchanged-tree records are `t485-host-gate-5-report.json` and `t485-host-gate-5-result.json`. Only this outcome narrative and the generated report are added after the verified tree; implementation, specs, tests and reviewed render inputs are unchanged. This proves the bounded platform UI slice, not completion of T-485's remaining authored-and-bound acceptance.
