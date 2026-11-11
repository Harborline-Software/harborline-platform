# T-594/template-editors mutation evidence

Producer prerequisite: `48e5a89a318f718f069b2af801c1159980bdb514` (#416), verified on fetched `origin/main`. Measured on Mac mini, 2026-11-10.

## Whole-module measurements

- Two sequential full StrykerJS runs (`node tooling/strykerjs.mjs run --all projections/react/ui/hlp.ui.document-authoring`): **82.68%, 82.68%**; each tested 381 mutants (313 killed, 66 survived, 2 timeouts). New baseline/break **82**, the floor of the lower score under the 2026-09-26 owner ruling. No existing baseline changed. Measurement commands completed their reports but returned 1 solely because the not-yet-recorded new module had no baseline; subsequent configuration check passes.
- Stryker.NET changed-file gate (`node tooling/stryker.mjs run hlp.ui.button.tests -- --concurrency 2`): **76.41%**, existing break **38**, 229 tested (172 killed, 54 survived, 3 timeouts), including generated Razor spans. No .NET baseline/configuration changed.

Report digests (JSON reports are local build output, not source):

- React full run 1: SHA-256 `6967f04607c3a5773510ce0a0f111f4c350d8ac01b42d0b7636e38ea34219209`.
- React full run 2: SHA-256 `3079dea91114cf26faf051c051161681ca231afdd4ae81d28c7a3518c1ab8a99`.
- Blazor changed-file run: SHA-256 `8bf10def09960018ae1f1f82f5bd51e92b967074accf6a61b1ff8a385121981f`.

## Review regression: text input before blur

- Test: `T-594 item 4: text input without blur survives incoming identity or revision and offers Keep Discard` (`UnblurredTextSurvivesIncomingDraft`), independently exercising replacement identity and replacement revision. Only `input` is dispatched; no `change` or blur precedes the host parameter update. Literal `Typing without blur` must remain visible and emitted, both pending-edit actions must be enabled, and explicit Discard must adopt the incoming draft.
- Named mutation: **text-input-delayed-until-change** — replace the shared text helper's `@oninput` with `@onchange`, the original implementation at `091113df04de03d725dc80f2672a01c6511e02cf`. Before the fix, both cases fail with `MissingEventHandlerException` for `oninput`; the component is not tracking input events. Both pass with immediate input handling. This is targeted mutation evidence, not a remeasurement of the aggregate scores above.
- The existing Blazor test driver now dispatches `input` for text controls and `change` for selects. Existing `ReadOnly`, `PendingEdits` and `StalePreview` tests therefore also exercise immediate text editing, including preview invalidation before blur.
- Oracle: the review's no-data-loss requirement and independent literal text, incoming identity/revision and visible Keep/Discard actions; no private component state or production constants supply expected values.

## React killing tests

Test file: `projections/react/ui/hlp.ui.document-authoring/src/DocumentAuthoringEditor.test.tsx`. Each row records one killed Stryker mutant, except the explicitly named reversible seeded faults where Stryker stopped at an earlier overlapping killing test. Source positions identify mutants without relying on unstable numeric IDs.

| Test (full display name) | Named recorded mutation |
| --- | --- |
| Document authoring documents-eng-21 documents-auth-1 documents-auth-3 documents-auth-4 documents-auth-5 documents-auth-7 documents-auth-10 documents-auth-15 documents-auth-16 documents-ck-5 documents-ck-6 documents-ck-23 layout-ck-43: creates the exact literal contract oracle from nothing | R0: src/DocumentAuthoringEditor.tsx:17, BlockStatement: `export function DocumentAuthoringEditor(props: DocumentAuthoringEditorProps) {   const [draft, setDraft] = useState(props.value)   const [incoming, setIncoming]` → `{}` |
| Document authoring documents-auth-5 layout-ck-43: orders blocks, lines and runs and removes only the selected item | R1: src/DocumentAuthoringEditor.tsx:41, ConditionalExpression: `position === index` → `true` |
| Document authoring T-594 item 4 documents-ck-23: only container sm/md/lg, no chrome window class, and booleans can be cleared | R2: src/DocumentAuthoringEditor.tsx:46, LogicalOperator: `value && !entries.some(entry => entry.id === value) && <option value={value}>{value} (not offered)</option>` → `value && !entries.some(entry => entry.id === value) \|\| <option value={value}>{value} (not offered)</option>` |
| Document authoring documents-ck-5 documents-auth-3: locale policy clears the old fixed tag without touching style or runs | R3: src/DocumentAuthoringEditor.tsx:66, ConditionalExpression: `draft.locale.kind === 'fixed'` → `true` |
| Document authoring documents-auth-15: all nine page master slots preserve the other assignments and clear only the chosen slot | seed-react-master-assignment: replace `[region]: value \|\| undefined` with `[region]: undefined`; this test failed its literal nine-slot expectation. |
| Document authoring T-594 item 4: string/object options, absent catalogue fallback and unoffered current options remain visible | R5: src/DocumentAuthoringEditor.tsx:45, ArrayDeclaration: `[]` → `["Stryker was here"]` |
| Document authoring T-594 item 4: readOnly disables all controls and emits no draft or preview | R6: src/DocumentAuthoringEditor.tsx:40, ConditionalExpression: `props.readOnly` → `false` |
| Document authoring T-594 item 4: a clean identity/revision reset adopts the host but a same-revision echo does not overwrite | R7: src/DocumentAuthoringEditor.tsx:16, EqualityOperator: `left.revision === right.revision` → `left.revision !== right.revision` |
| Document authoring T-594 item 4: dirty identity and revision replacements require Keep or Discard, including readOnly pending handling | R8: src/DocumentAuthoringEditor.tsx:16, ConditionalExpression: `left.revision === right.revision` → `true` |
| Document authoring documents-eng-21: denials are scoped to identity, revision and local generation, not echoed onto later edits | R9: src/DocumentAuthoringEditor.tsx:40, CallExpression: `setGeneration(nextGeneration);` → `;` |
| Document authoring documents-eng-21 documents-auth-17 documents-run-2: requests only a non-minting server preview with the supplied flow and a labelled sample | R10: src/DocumentAuthoringEditor.tsx:23, StringLiteral: `'sample'` → `""` |
| Document authoring documents-auth-17: record preview clears a previous sample and never chooses a target automatically | R11: src/DocumentAuthoringEditor.tsx:82, StringLiteral: `''` → `"Stryker was here!"` |
| Document authoring documents-auth-3 documents-auth-17 layout-ck-43: displays host-rendered literal and fallback runs under document locale en-US | seed-react-preview-language: replace `lang={preview.localeTag}` with `lang="ja-JP"`; both en-US and fr-FR rows failed their literal language expectation. |
| Document authoring documents-auth-3 documents-auth-17 layout-ck-43: displays host-rendered literal and fallback runs under document locale fr-FR | seed-react-preview-language: replace `lang={preview.localeTag}` with `lang="ja-JP"`; both en-US and fr-FR rows failed their literal language expectation. |
| Document authoring documents-run-2: absent preview authority refuses, failure is retryable, and a minted response is never displayed | R14: src/DocumentAuthoringEditor.tsx:55, ConditionalExpression: `result.minted !== false` → `false` |
| Document authoring documents-run-2 T-594 item 4: in-flight previews cannot overwrite an edited draft, replacement or revoked authority | R15: src/DocumentAuthoringEditor.tsx:31, BlockStatement: `{ serial.current++; setPreview(undefined); setStatus('') }` → `{}` |
| Document authoring documents-run-2 T-594 item 4: target flow and pending replacement invalidate outstanding preview requests | R16: src/DocumentAuthoringEditor.tsx:33, BlockStatement: `{ resetPreview() }` → `{}` |
| Document authoring documents-eng-21: the screen renders the editor through the shared host shell | R17: src/DocumentAuthoringScreen.tsx:5, BlockStatement: `{ return <>{shell.render(<DocumentAuthoringEditor {...editor} />)}</> }` → `{}` |

## Blazor killing tests

Test file: `projections/blazor/ui/hlp.ui.document-authoring.tests/DocumentAuthoringTests.cs`. The canonical aggregate uses coverage-analysis off for MemberData, so per-test evidence comes from reversible seeded mutations and TRX results, not invented report attribution.

- **seed-blazor-readonly-inversion**: in `Change`, `if (ReadOnly) return` → `if (!ReadOnly) return`. Full module suite: 18 failed, 2 passed.
- **seed-blazor-preview-authority-inversion**: in `RenderPreview`, `ReadOnly || !CanPreview` → `ReadOnly || CanPreview`; `PreviewDenial` failed.
- **seed-blazor-shell-bypass**: `@Shell(Editor)` → `@Editor`; `ScreenUsesHostShell` failed.
- All seeds were restored byte-for-byte before clean validation. The initial preview-suppression experiment did not compile (nullable callback); it is **not** counted as killed-test evidence.

| Test (full display name, including theory row) | Named recorded mutation |
| --- | --- |
| documents-eng-21: authoring screen places the editor in the host supplied shell | seed-blazor-shell-bypass |
| T-594 item 4 documents-ck-23: sm md lg container tokens and semantic breaks never depend on chrome window classes | seed-blazor-readonly-inversion |
| T-594 item 4: readOnly emits no changes or preview even when events are dispatched | seed-blazor-readonly-inversion |
| documents-auth-15: all nine page master slots preserve their neighbours and can be cleared | seed-blazor-readonly-inversion |
| T-594 item 4 documents-ck-23: authored container tokens pass shared numeric admission; illegal numeric constraints still refuse(token: "sm") | seed-blazor-readonly-inversion |
| layout-ck-43 documents-auth-7 documents-auth-10 documents-auth-17 documents-run-2: authored runs use DocumentRuntime non-minting preview under two document locales(locale: "fr-FR") | seed-blazor-readonly-inversion |
| T-594 item 4: clean identity revision resets, dirty Keep Discard and readOnly pending transitions | seed-blazor-readonly-inversion |
| documents-run-2: denied authority failure retry and minted responses fail closed | seed-blazor-preview-authority-inversion |
| documents-run-2 T-594 item 4: stale async previews never replace a later edit or revoked authority | seed-blazor-readonly-inversion |
| layout-ck-43 documents-auth-7 documents-auth-10 documents-auth-17 documents-run-2: authored runs use DocumentRuntime non-minting preview under two document locales(locale: "en-US") | seed-blazor-readonly-inversion |
| T-594 item 4 documents-ck-23: authored container tokens pass shared numeric admission; illegal numeric constraints still refuse(token: "md") | seed-blazor-readonly-inversion |
| T-594 item 4 documents-ck-23: authored container tokens pass shared numeric admission; illegal numeric constraints still refuse(token: "lg") | seed-blazor-readonly-inversion |
| documents-eng-21 documents-auth-1 documents-auth-3 documents-auth-4 documents-auth-5 documents-auth-7 documents-auth-10 documents-auth-15 documents-auth-16 documents-ck-5 documents-ck-6 documents-ck-23 layout-ck-43: author from nothing matches the independent literal fixture | seed-blazor-readonly-inversion |
| documents-eng-21: server denials are fenced to identity revision and local generation | seed-blazor-readonly-inversion |
| documents-run-2 T-594 item 4: target flow pending replacement and readOnly invalidate outstanding preview requests | seed-blazor-readonly-inversion |
| documents-eng-21 documents-auth-17 documents-run-2: server preview carries the authored draft supplied page flow and labelled sample | seed-blazor-readonly-inversion |
| documents-auth-5 layout-ck-43: ordered blocks, lines and runs remove exactly the selected item | seed-blazor-readonly-inversion |
| documents-ck-5 documents-auth-3: changing locale policy clears only the fixed tag | seed-blazor-readonly-inversion |
| T-594 item 4: string and object options, missing sections and unoffered current values | seed-blazor-readonly-inversion |
| documents-eng-21: shared literal cross-lane contract fixture(caseId: "(native)", rawFixture: null) | seed-blazor-readonly-inversion |

## Oracle sources

Gallery regression: `document-authoring.refusals is accessible and visually conformant` caught 139px horizontal overflow at the unchanged 320px/200% reflow check before the shared CSS fix. Giving the editor a zero-minimum grid track and inherited `overflow-wrap: anywhere` fixes the actual long diagnostic text in both lanes. All four scenarios subsequently pass the existing Axe, reflow, visual and ARIA-snapshot parity checks; no viewport, rule or snapshot changed.

The from-nothing and shared-fixture tests replay public controls and compare against the independent literal neutral fixture in `conformance/hlp.ui.document-authoring/fixtures.yaml`, grounded in DES-0021 and the existing Layout text-run/page-master/break contracts. Ordering, options, readonly and pending-edit tests assert explicit literal values or the property that no unauthorized/stale event may escape. React locale rows assert literal en-US/fr-FR and fallback text from a separately supplied host result. Blazor runtime rows compose actual authored Layout definitions and call the canonical `DocumentRuntime.PreviewAsync`; null, empty string, raw text and document languages have independently written literal expectations. Numeric tests invoke shared Layout admission, accepting sm/md/lg and refusing columnCount 0. No expected value is read from the production symbol it asserts.
