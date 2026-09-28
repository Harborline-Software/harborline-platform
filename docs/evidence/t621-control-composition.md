# T-621 control composition: reuse assessment

Date: 2026-09-19. Status: implemented and focused verification passed; fresh visual approval and the full repository gate remain pending. This is not a gate receipt or an owner design approval.

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

Sandbox preview restore again hit the known NuGet TLS/signature endpoint error after library checks passed. The previously authorized host-only preview preparation completed with audit enabled; no warning or freshness check was disabled. The focused proof does not replace the platform gate. The changed scrolling surface remains available for Chris's review; no new approval digest or full-gate receipt is claimed.
