# Platform Components — Terminal Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Drive all 76 `hlp.ui.*` modules to a terminal, gate-enforced status by finishing ticket 098's Tier-1 gate model and closing the four component tickets (090, 101, 102, 106).

**Architecture:** The gate model's scanners currently live in `harborline-control/tools/` and are run by hand. Nothing in `harborline-platform` can fail because of them, so no module status can mean anything. This plan **vendors the harness into the platform**, wires it in as a required phase-4 gate step producing a recorded receipt, then makes `catalog/modules.yaml`'s module `status` field validated against that receipt. Only then does the remaining work — building four unbuilt gates and dispositioning 76 modules — produce a status that cannot be claimed without being earned.

**Tech Stack:** Node 24 ESM (`.mjs`, no build step, no test framework — `--canary` self-checks), JSON-in-`.yaml` catalog files, Playwright/Storybook gallery, vitest (React lane), bUnit 2.9.0 (Blazor lane), .NET 11 preview 7.

## Global Constraints

- **Vendoring precedent:** `eng/run-exact-clone.mjs` was vendored out of `harborline-migration` into `harborline-api` for exactly this reason and says so in its own header. Copy that discipline: the vendored file carries a header naming where it came from and why.
- **Every check ships its canary in the same commit.** A gate whose canary passes is reported DEAD, not green (`assertGateCanFail`, ticket 098 acceptance 3).
- **A canary must go through the same code path the report uses.** Ticket 098 records a canary that computed VOID through its own local copy of the rule and stayed green when the real propagation was broken. Call `gateRows()`, never a reimplementation.
- **Tooling never writes a human verdict.** `record-design-verdict.mjs` refuses to invent a reviewer, and that refusal is load-bearing — `assertDesignReview` is defined as a recorded *human* judgement.
- **No module status may be inferred by a sweep.** Ticket 098 acceptance 5: "The sweep produces the worksheet; it does not get to answer it." Every `not-applicable` carries a rationale and a named decider.
- **`requiredStepIds` lives in exactly one place** — `tooling/gate-contract.mjs`. Adding a step invalidates every existing phase-4 receipt; regenerate, do not hand-edit `gate.json`.
- **All four repos are plain git** (no GitButler state). Use `git` directly.
- **`gallery/styles/canvas.css` and `gallery/projections/blazor/wwwroot/gallery.css` must stay byte-identical** — `tooling/validate-gallery.mjs` asserts it. Change both together.
- **`catalog/ui-theme-registry.json` is pinned to an exact Harborline App source blob.** Do not add `publicTokens`; the supported extension point is `galleryAliases`, and `canvas.css` may define non-registry tokens (precedent: `--hl-button-warning`, `--hl-selected`, `--hl-hover`, `--hl-warning`).
- **Write files with LF endings.** These repos are LF-only.
- **GitHub Actions is off by design** (`ACTIONS_ENABLED` absent = off). `npm run gate:phase4` then `npm run receipt:phase4` IS the verification story. Do not propose CI or billing changes.

---

## Measured starting state (2026-08-25, `harborline-platform` @ `2bd0847`)

Every number below was produced by running the tool named, not read from a ticket.

```
node tools/scan-token-resolution.mjs      FAIL 0   WARN 140 across 57 tokens
node tools/scan-declaration-gaps.mjs      59 modules with scenarios; 38 no states, 53 no empty,
                                          55 no error, 53 no content; 29 declare none of the four
node tools/scan-scenario-determinism.mjs  2 of 59 modules: search-input (2), toaster (1)
node tools/run-vertical-pair.mjs          FAIL=1 PARTIAL=5 PASS=9 UNBUILT=11 VOID=2
```

Catalog: 76 `hlp.ui.*` modules — **59 `presentation: visual`**, **17 `presentation: not-applicable`**
(hooks, class composition, string catalogs). Module status is **51 `extracted-candidate` + 25
`gap-implemented`** and is **validated by nothing** — `tooling/validate-repository.mjs:273` checks
*projection* status against `allowedStatuses`, and never looks at module status at all.

### The four tickets, as measured rather than as the register records them

The register lists all four as `ready`. Three are not.

| ticket | register | measured | what actually remains |
|---|---|---|---|
| 090 detail-panel heading levels | ready | **DONE** | nothing — close it (Task 3) |
| 101 Blazor persistent toast | ready | **code landed** | acceptance 3, the divergent-parameter sweep (Task 5) |
| 102 scenario catalog drift | ready | **DONE** | nothing — the ticket carries its own Outcome section (Task 3) |
| 106 undefined design tokens | ready | **FAIL 0** | the 140 WARN dispositions (Task 6) |

Evidence for the two DONE calls:

- **090** — both lanes emit `<h2>` for in-panel section headings in all four pillars
  (`apps/react/src/admin/{reports,views,data-exchange,scheduling}/*Detail.tsx` and
  `apps/blazor/Admin/{Reports,Views,DataExchange,Scheduling}/*DetailView.razor`), and the pinning
  test acceptance 3 asks for exists **in both lanes** — vitest
  (`__tests__/*AdminPage.detail.test.tsx`, `getByRole('heading', { level: 2, … })`) and bUnit
  (`*AdminPageTests.cs`, `panel.QuerySelectorAll("h2")`). The ticket's step-1 question is also
  answered: `hlp.ui.detail-panel` renders `<aside aria-label>` / `<div role="dialog" aria-label>`
  and **contributes no heading element**, so the page `<h1>` is the only outline node above the
  panel and `<h2>` is the correct level.
- **101** — `ShipyardToaster.razor:31` is `[Parameter] public int? DurationMilliseconds`, and
  `ToastTypes.cs:8` `ToastHostConfiguration` takes `int? DurationMilliseconds = 4000`. The
  `toaster.action-promise` fixture already carries `"duration": null` and
  `DurationMilliseconds="@((int?)null)"`.

### The one standing FAIL is a scanner defect, not a component defect

`run-vertical-pair.mjs` reports `assertDeterminism FAIL — lane-asymmetric async construct in
toaster.action-promise (react:Promise)`, which VOIDs `assertVisualParity` and
`assertFunctionalParity` for that module. The scenario's two lanes are:

```
react  : void service.trackAsync(Promise.resolve('indexed'), …)
blazor : _ = Toasts.TrackAsync(Task.FromResult("indexed"), …)
```

`Task.FromResult` is the exact twin of `Promise.resolve`. `ASYNC_PATTERNS` in
`scan-scenario-determinism.mjs:89-101` carries `['Promise', /\bPromise\s*\./]` for the JS lane and,
for the C# lane, only `Task.Delay` and `DateTime.Now`. **The asymmetry is in the pattern list, not
in the fixture** — the scanner cannot see the Blazor half, so it reports a lane divergence that does
not exist. This is the house defect pattern: a check that reads one thing and answers a different
question.

---

## File structure

**New — `harborline-platform/tooling/gates/` (the vendored harness):**

| file | responsibility |
|---|---|
| `gate-rows.mjs` | `GATES`, `gateRows(ctx)`, `contextFor(root, moduleId)`. The ONE place a verdict or a VOID is computed. |
| `design-review.mjs` | Verdict record shape, `referenceRevision()`, expiry rule. Vendored verbatim. |
| `derive-state-set.mjs` | Derives the required state set from the TS and C# type surfaces. Vendored verbatim. |
| `scan-scenario-determinism.mjs` | `assertDeterminism`, cheap half. Vendored, then fixed in Task 4. |
| `scan-token-resolution.mjs` | `assertTokenAdherence`, resolution half. Vendored verbatim. |
| `scan-declaration-gaps.mjs` | `assertEmptyAndErrorStates` / `assertContentResilience` / `assertStateCompleteness` presence. Vendored verbatim. |
| `scan-catalog-story-drift.mjs` | Ticket 102's landed check. Vendored verbatim. |
| `scan-nullable-parity.mjs` | **New** (Task 5) — Blazor `[Parameter]` vs React `T \| null`. |
| `run-ui-gate-model.mjs` | **New** (Task 2) — runs `gateRows` over all 76 modules, writes the receipt. |
| `run-vertical-pair.mjs` | The two-module deep report. Vendored, reduced to a thin wrapper over `gate-rows.mjs`. |
| `verify-canaries.sh` | `assertGateCanFail` over every scanner. Vendored, path-adjusted. |

**New — evidence:**

- `docs/evidence/gate-model/ui-gate-model.json` — the receipt the validator reads.
- `docs/evidence/design-review/<moduleId>.json` — moved from `harborline-control/evidence/design-review/`.

**Modified:**

- `tooling/gate-contract.mjs` — add `ui-gate-model` to `requiredStepIds`.
- `tooling/run-phase-4-gate.mjs` — run the new step.
- `tooling/validate-repository.mjs` — validate module status against the receipt.
- `package.json` — `gates:ui` and `gates:canaries` scripts.
- `specs/modules/ui/*/quality.yaml` — `gates` dispositions; `largeData` → `large-data`.
- `specs/modules/ui/*/scenarios.json` — empty / error / content scenario declarations.

**Deleted after vendoring:** the seven `harborline-control/tools/` scanners and
`harborline-control/evidence/design-review/`, replaced by a `README.md` pointer. A tool in two
places is a second register.

---

## Phase A — the harness moves into the platform and can fail there

### Task 1: Vendor the gate-model harness into `tooling/gates/`

Nothing else in this plan can be enforced until the scanners live where the gate runs. This task
moves them and proves they still work, changing no logic.

**Files:**
- Create: `tooling/gates/{design-review,derive-state-set,scan-scenario-determinism,scan-token-resolution,scan-declaration-gaps,scan-catalog-story-drift,run-vertical-pair}.mjs`
- Create: `tooling/gates/verify-canaries.sh`
- Create: `docs/evidence/design-review/hlp.ui.badge.json` (moved)
- Create: `tooling/gates/README.md`
- Modify: `package.json` (scripts)
- Delete (in `harborline-control`): `tools/{design-review,derive-state-set,record-design-verdict,scan-scenario-determinism,scan-token-resolution,scan-declaration-gaps,scan-catalog-story-drift,run-vertical-pair}.mjs`, `tools/verify-canaries.sh`, `evidence/design-review/`

**Interfaces:**
- Produces: `tooling/gates/design-review.mjs` exports `recordsRoot`, `referenceRevision(platformRoot, moduleId) -> string|null`, `loadRecord(moduleId, root?) -> object|null`, `reviewVerdict({record, revision}) -> [status, note]`
- Produces: `tooling/gates/derive-state-set.mjs` exports the state-set derivation used by `assertStateCompleteness`
- Produces: each `scan-*.mjs` supports `--canary` and `--json`, and defaults its platform root to the repository root rather than an absolute path

- [ ] **Step 1: Record the pre-move baseline so the move can be proven lossless**

```bash
cd /c/Projects/Harborline/harborline-control/tools
for t in scan-scenario-determinism scan-token-resolution scan-declaration-gaps scan-catalog-story-drift; do
  node "$t.mjs" --json > "/tmp/before-$t.json" 2>/dev/null || echo "no --json: $t"
done
node run-vertical-pair.mjs > /tmp/before-vertical-pair.txt
tail -3 /tmp/before-vertical-pair.txt
```

Expected: the tally line reads `tally: FAIL=1  PARTIAL=5  PASS=9  UNBUILT=11  VOID=2`.

- [ ] **Step 2: Copy the eight files into the platform**

```bash
cd /c/Projects/Harborline
mkdir -p harborline-platform/tooling/gates harborline-platform/docs/evidence/design-review
cp harborline-control/tools/{design-review,derive-state-set,record-design-verdict,scan-scenario-determinism,scan-token-resolution,scan-declaration-gaps,scan-catalog-story-drift,run-vertical-pair}.mjs harborline-platform/tooling/gates/
cp harborline-control/tools/verify-canaries.sh harborline-platform/tooling/gates/
cp harborline-control/evidence/design-review/*.json harborline-platform/docs/evidence/design-review/
ls harborline-platform/tooling/gates/
```

- [ ] **Step 3: Repoint the two paths that assumed the control repo**

`design-review.mjs` resolves its records relative to itself. In the control repo that was
`../evidence/design-review`; in the platform it is `../../docs/evidence/design-review`.

```javascript
// tooling/gates/design-review.mjs
const here = dirname(fileURLToPath(import.meta.url))
export const recordsRoot = resolve(here, '../../docs/evidence/design-review')
```

Every `scan-*.mjs` and `run-vertical-pair.mjs` defaults `platformRoot` to a hardcoded absolute
path. Replace that default in each file — the tool now lives inside the tree it scans:

```javascript
const here = dirname(fileURLToPath(import.meta.url))
const platformRoot = positional[0] ?? resolve(here, '../..')
```

- [ ] **Step 4: Add the vendoring header to every copied file**

Paste this at the top of each of the nine copied files, above the existing header comment, with
`<TOOL>` replaced by the file's own name:

```javascript
// Vendored from harborline-control/tools/<TOOL> on 2026-08-25.
//
// It measured this repository from outside it, so nothing here could ever fail because of it and no
// module status it produced could mean anything. Same reason eng/run-exact-clone.mjs was vendored
// into harborline-api. The control-repo copy is deleted, not forked: a tool in two places is a
// second register, and this programme has already paid for one of those.
```

- [ ] **Step 5: Add the npm scripts**

```json
"gates:ui": "node tooling/gates/run-ui-gate-model.mjs",
"gates:pair": "node tooling/gates/run-vertical-pair.mjs",
"gates:canaries": "bash tooling/gates/verify-canaries.sh"
```

(`gates:ui` is written in Task 2; adding all three now keeps `package.json` touched once.)

- [ ] **Step 6: Run the canaries in their new home**

Run: `bash tooling/gates/verify-canaries.sh`

Expected: one `OK` line per `scan-*.mjs`, exit 0:

```
OK   scan-catalog-story-drift.mjs — canary green, and red when discovery is broken
OK   scan-declaration-gaps.mjs — canary green, and red when discovery is broken
OK   scan-scenario-determinism.mjs — canary green, and red when discovery is broken
OK   scan-token-resolution.mjs — canary green, and red when discovery is broken
```

Any `DEAD` line means the perturbation did not reach that scanner — fix the path, do not proceed.

- [ ] **Step 7: Prove the move was lossless**

```bash
cd /c/Projects/Harborline/harborline-platform
node tooling/gates/run-vertical-pair.mjs | tail -3
```

Expected: the identical tally from step 1 — `tally: FAIL=1  PARTIAL=5  PASS=9  UNBUILT=11  VOID=2`.
A different tally means a path repoint changed what a scanner can see; diff against
`/tmp/before-vertical-pair.txt` and fix before committing.

- [ ] **Step 8: Delete the control-repo originals and leave a pointer**

```bash
cd /c/Projects/Harborline/harborline-control
git rm -q tools/{design-review,derive-state-set,record-design-verdict,scan-scenario-determinism,scan-token-resolution,scan-declaration-gaps,scan-catalog-story-drift,run-vertical-pair}.mjs tools/verify-canaries.sh
git rm -q -r evidence/design-review
```

Append to `harborline-control/tools/README.md`:

```markdown
## Moved to harborline-platform (2026-08-25)

The ticket-098 gate-model scanners now live in `harborline-platform/tooling/gates/` and run as the
`ui-gate-model` phase-4 gate step. They measured that repository from outside it, so nothing there
could fail because of them. Design-review verdict records moved with them, to
`harborline-platform/docs/evidence/design-review/`.
```

- [ ] **Step 9: Commit both repos**

```bash
cd /c/Projects/Harborline/harborline-platform && git add -A && git commit -m "The gate-model scanners move into the tree they measure"
```

```bash
cd /c/Projects/Harborline/harborline-control && git add -A && git commit -m "The gate-model scanners leave; the pointer stays"
```

---

### Task 2: Make `ui-gate-model` a required phase-4 gate step

The harness now lives in the platform but still runs only by hand. This task gives it a receipt and
makes the gate refuse without one.

**Files:**
- Create: `tooling/gates/gate-rows.mjs`
- Create: `tooling/gates/run-ui-gate-model.mjs`
- Modify: `tooling/gates/run-vertical-pair.mjs` (import `gateRows` instead of defining it)
- Modify: `tooling/gate-contract.mjs:16-20`
- Modify: `tooling/run-phase-4-gate.mjs` (the `try` block at ~line 139)
- Create: `docs/evidence/gate-model/ui-gate-model.json`

**Interfaces:**
- Consumes: `design-review.mjs` (`referenceRevision`, `loadRecord`), `derive-state-set.mjs`, the four `scan-*.mjs`
- Produces: `gate-rows.mjs` exports `GATES` (array), `gateRows(ctx) -> [{id, parent, status, note}]`, `contextFor(platformRoot, moduleId) -> ctx`, `TIER1_GATE_IDS` (string[])
- Produces: `run-ui-gate-model.mjs` writes `docs/evidence/gate-model/ui-gate-model.json` with shape
  `{schemaVersion: 1, subject: {moduleCount}, modules: [{moduleId, presentation, terminal, gates: [{id, status, note}]}], tally: {PASS, PARTIAL, FAIL, VOID, UNBUILT, "NOT-APPLICABLE"}}`

- [ ] **Step 1: Extract `gateRows` out of `run-vertical-pair.mjs` into `gate-rows.mjs`**

Move `GATES`, `VOIDED_BY_DETERMINISM`, `verdictOf`, `gateRows`, `rollUp`, `contextFor`,
`scannerRows`, `tokenSummary`, `catalogFor` and `qualityFor` verbatim into `tooling/gates/gate-rows.mjs`
and export them. `run-vertical-pair.mjs` keeps only its report formatting, its `--canary`, and:

```javascript
import {GATES, gateRows, contextFor, TIER1_GATE_IDS} from './gate-rows.mjs'
```

Add to `gate-rows.mjs`:

```javascript
// The Tier-1 set, in report order. A module is terminal only when every one of these is PASS or
// NOT-APPLICABLE; assertDesignQuality is the rollup and is included so a module cannot go terminal
// while its rollup is UNBUILT.
export const TIER1_GATE_IDS = GATES.map(gate => gate.id)
```

- [ ] **Step 2: Write the failing check first — a canary that says the receipt is not written yet**

Create `tooling/gates/run-ui-gate-model.mjs` with only its canary implemented:

```javascript
// assertGateCanFail for the receipt itself: a module claiming terminal status while carrying a
// non-terminal gate row must be refused. Goes through terminalFor(), the same function the receipt
// uses -- a canary computing the rule locally reports a DEAD gate as healthy (ticket 098).
function canary() {
  const failures = []
  const green = TIER1_GATE_IDS.map(id => ({id, status: 'PASS', note: ''}))
  if (!terminalFor(green)) failures.push('an all-PASS row must be terminal')
  for (const status of ['PARTIAL', 'FAIL', 'VOID', 'UNBUILT']) {
    const row = [...green.slice(1), {id: TIER1_GATE_IDS[0], status, note: ''}]
    if (terminalFor(row)) failures.push(`a row containing ${status} must NOT be terminal`)
  }
  const naRow = green.map(g => ({...g, status: 'NOT-APPLICABLE'}))
  if (!terminalFor(naRow)) failures.push('an all-NOT-APPLICABLE row must be terminal')
  if (failures.length) { console.error('canary FAIL:\n  ' + failures.join('\n  ')); process.exit(1) }
  console.log('canary OK -- PASS and NOT-APPLICABLE are terminal; PARTIAL, FAIL, VOID and UNBUILT are not')
}
```

Run: `node tooling/gates/run-ui-gate-model.mjs --canary`
Expected: FAIL with `ReferenceError: terminalFor is not defined`.

- [ ] **Step 3: Implement `terminalFor` and the receipt writer**

```javascript
import {readFileSync, mkdirSync, writeFileSync} from 'node:fs'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'
import {gateRows, contextFor, TIER1_GATE_IDS} from './gate-rows.mjs'

const here = dirname(fileURLToPath(import.meta.url))
const root = resolve(here, '../..')

const TERMINAL = new Set(['PASS', 'NOT-APPLICABLE'])

// A status a person declared not-applicable counts as earned; every other status does not. PARTIAL
// is deliberately NOT terminal: "declared but not rendered" is the state 53 modules are in, and
// admitting it would make the status mean "someone wrote a scenario id".
export function terminalFor(rows) {
  return rows.length > 0 && rows.every(row => TERMINAL.has(row.status))
}

function build() {
  const catalog = JSON.parse(readFileSync(resolve(root, 'catalog/modules.yaml'), 'utf8')).modules
  const moduleIds = Object.keys(catalog).filter(id => id.startsWith('hlp.ui.')).sort()
  const tally = {}
  const modules = moduleIds.map(moduleId => {
    const rows = gateRows(contextFor(root, moduleId))
      .map(({id, status, note}) => ({id, status, note}))
    for (const row of rows) tally[row.status] = (tally[row.status] ?? 0) + 1
    return {
      moduleId,
      presentation: catalog[moduleId].presentation?.disposition ?? 'unknown',
      terminal: terminalFor(rows),
      gates: rows,
    }
  })
  return {schemaVersion: 1, subject: {moduleCount: modules.length}, modules, tally}
}

if (process.argv.includes('--canary')) canary()
else {
  const receipt = build()
  mkdirSync(resolve(root, 'docs/evidence/gate-model'), {recursive: true})
  writeFileSync(resolve(root, 'docs/evidence/gate-model/ui-gate-model.json'),
    JSON.stringify(receipt, null, 2) + '\n')
  const terminal = receipt.modules.filter(m => m.terminal).length
  console.log(`${receipt.modules.length} modules, ${terminal} terminal`)
  console.log(Object.entries(receipt.tally).sort().map(([k, v]) => `${k}=${v}`).join('  '))
  process.exitCode = 0
}
```

Note the exit code: the step records state, it does not fail on a non-terminal module. The
**validator** is what refuses a status claim (Task 13). A gate step that went red on 76
non-terminal modules would be red for the whole life of this plan and would be routed around.

- [ ] **Step 4: Run the canary to verify it passes**

Run: `node tooling/gates/run-ui-gate-model.mjs --canary`
Expected: `canary OK -- PASS and NOT-APPLICABLE are terminal; PARTIAL, FAIL, VOID and UNBUILT are not`

- [ ] **Step 5: Generate the first receipt**

Run: `node tooling/gates/run-ui-gate-model.mjs`

Expected: `76 modules, 0 terminal` and a tally line. Zero terminal is the correct starting point —
every module has at least one UNBUILT gate today.

- [ ] **Step 6: Add the step to the gate contract**

`tooling/gate-contract.mjs` — insert `'ui-gate-model'` between `'tooling-selftests'` and `'build'`.
It is a static sweep over source; it belongs with the other cheap checks, before the 38-minute half:

```javascript
export const requiredStepIds = [
  'npm-clean-install', 'forms-contracts-clean-install', 'rule-runtime-clean-install', 'rule-authoring-clean-install', 'copilot-contracts-clean-install', 'dotnet-restore', 'generation-smoke', 'catalog-preflight',
  'tooling-selftests', 'ui-gate-model', 'build', 'native-tests', 'ui-shared-conformance', 'package-consumers',
  'gallery-gate', 'catalog-final',
]
```

`tooling/run-phase-4-gate.mjs` — add the matching `run()` call in the same position:

```javascript
  run('tooling-selftests', process.execPath, ['tooling/run-tooling-selftests.mjs'], root, true)
  run('ui-gate-model', process.execPath, ['tooling/gates/run-ui-gate-model.mjs'], root, true)
  run('build', process.execPath, ['tooling/run-native.mjs', '--build'], root, true)
```

- [ ] **Step 7: Regenerate the gate and the receipt**

```bash
npm run gate:phase4 && npm run receipt:phase4
```

Expected: `status: PASS` with 16 steps. The step order is asserted positionally
(`results[index]?.id === id`), so a mismatch here means the `run()` call is in the wrong place.

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "The gate model gets a receipt, and the phase-4 gate a step that writes it"
```

---

## Phase B — reconcile the register with what is already true

### Task 3: Close tickets 090 and 102

Both are complete. Their register rows say `ready`, which is how a finished piece of work gets done
twice. Closing them is the whole task; no code changes.

**Files:**
- Modify: `harborline-control/tickets/README.md` (the 090 and 102 rows)
- Modify: `harborline-control/tickets-done/T-090-detail-heading-levels-disagree-between-lanes/ticket.md`
- Modify: `harborline-control/tickets-done/T-102-the-scenario-catalog-describes-code-it-cannot-check/ticket.md`

- [ ] **Step 1: Re-verify 090 in both lanes before closing it**

```bash
cd /c/Projects/Harborline/harborline-app
grep -rn "<h2" apps/react/src/admin --include=*.tsx | grep -c .
grep -rn "<h2" apps/blazor/Admin --include=*.razor | grep -c .
grep -rn "level: 2" apps/react/src/admin --include=*.test.tsx | grep -c .
grep -rn 'QuerySelectorAll("h2")' tests/Harborline.App.Blazor.Tests | grep -c .
```

Expected: `7`, `7`, `7`, `4`. Both lanes emit `<h2>` in all four pillars, and both lanes pin it.
Any lower count means a pillar drifted since this plan was written — fix that pillar before closing.

- [ ] **Step 2: Run the positive control acceptance 2 requires**

```bash
cd /c/Projects/Harborline/harborline-app
sed -i 's|<h2>Parameters</h2>|<h3>Parameters</h3>|' apps/react/src/admin/reports/ReportDefinitionDetail.tsx
npx vitest run apps/react/src/admin/reports/__tests__/reportsAdminPage.detail.test.tsx
```

Expected: FAIL — `Unable to find an accessible element with the role "heading" and name "Parameters"`.

```bash
git checkout apps/react/src/admin/reports/ReportDefinitionDetail.tsx
npx vitest run apps/react/src/admin/reports/__tests__/reportsAdminPage.detail.test.tsx
```

Expected: PASS. A pinning test nobody has watched fail is not a pin.

- [ ] **Step 3: Record the outcome in ticket 090**

Append to `tickets-done/T-090-detail-heading-levels-disagree-between-lanes/ticket.md`:

```markdown
## Outcome — closed (2026-08-25)

**The correct level is `<h2>`, and step 1's question has an answer.** `hlp.ui.detail-panel` renders
`<aside aria-label>` in its inline form and `<div role="dialog" aria-label>` in its overlay form. It
contributes **no heading element** to the document outline — only an accessible name. So the page
`<h1>` is the nearest outline ancestor of the panel's contents, and panel sections start at `<h2>`.
The React lane's `<h3>` was the wrong half of the divergence, not the Blazor lane's `<h2>`, which is
the opposite of what "the React lane is the parity authority" would have suggested. Reading the
component rather than counting lanes is what the ticket asked for and why.

| acceptance | state |
|---|---|
| 1 — both lanes, same level, all four pillars | **MET** — `<h2>` in Reports, Views, Data Exchange and Scheduling, both lanes |
| 2 — positive control observed | **MET** — perturbing `ReportDefinitionDetail.tsx` to `<h3>` turns `reportsAdminPage.detail.test.tsx` red on the missing level-2 heading; reverting restores green |
| 3 — if the level is neither h2 nor h3, say so | **N/A** — it is `<h2>`, for the outline reason above |

Pinned in both lanes: `getByRole('heading', { level: 2, name })` in the four vitest
`__tests__/*AdminPage.detail.test.tsx` files, and `panel.QuerySelectorAll("h2")` in the four bUnit
`*AdminPageTests.cs` files. The next pillar copy cannot drift silently.
```

- [ ] **Step 4: Update the register rows**

In `tickets/README.md`, change the `090` and `102` rows' status column from `ready` to `DONE`.
Ticket 102 already carries its own `## Outcome — option 2 landed (2026-08-24)` section recording all
three acceptances met; only its register row was stale.

- [ ] **Step 5: Commit**

```bash
cd /c/Projects/Harborline/harborline-control && git add -A && git commit -m "090 and 102 were finished; the register catches up"
```

---

## Phase C — close the standing FAIL and the two genuinely-open tickets

### Task 4: Fix the determinism scanner's lane-asymmetric pattern list

Closes the only FAIL in the vertical pair and un-VOIDs two cells, without touching a component.

**Files:**
- Modify: `tooling/gates/scan-scenario-determinism.mjs:89-101` (`ASYNC_PATTERNS`) and its `--canary`

**Interfaces:**
- Consumes: nothing new
- Produces: `ASYNC_PATTERNS` entries become `[name, reactPattern, blazorPattern]` triples so a
  construct is named once and matched per lane; `scanScenario` reports a construct under one shared
  name whichever lane it appears in

- [ ] **Step 1: Write the failing canary case**

In the scanner's `canary()`, add a fixture whose two lanes are twins:

```javascript
  // A twin pair: Promise.resolve and Task.FromResult are the same construct in two languages. The
  // pattern list named only the JS half, so every scenario using both lanes correctly reported a
  // lane asymmetry that was in the pattern list rather than in the fixture -- and toaster's
  // assertDeterminism FAIL voided its visual and functional gates on that basis.
  const twins = {
    id: 'canary.twins',
    content: {props: {}, body: [{control: 'raw',
      react: "void service.trackAsync(Promise.resolve('x'), {})",
      blazor: '_ = Toasts.TrackAsync(Task.FromResult("x"), "l", _ => "s", _ => "e");'}]},
  }
  const twinResult = scanScenario(twins)
  const twinLanes = new Set(twinResult.async.map(entry => entry.lane))
  if (twinResult.async.length === 0) failures.push('twins: a settled-promise construct must be detected')
  if (twinLanes.size !== 2) failures.push(`twins: both lanes must report the construct, got ${[...twinLanes].join(',') || 'none'}`)
```

- [ ] **Step 2: Run the canary to verify it fails**

Run: `node tooling/gates/scan-scenario-determinism.mjs --canary`
Expected: FAIL with `twins: both lanes must report the construct, got react`

- [ ] **Step 3: Give every construct both lanes' spelling**

Replace `ASYNC_PATTERNS` with per-lane triples. `null` means the construct has no counterpart in
that language, which is a real answer and different from "nobody wrote the pattern":

```javascript
// Constructs in a scenario's raw body that resolve on their own schedule rather than on render.
//
// Each row names ONE construct and gives its spelling in each lane, because a construct present in
// both lanes is not a lane asymmetry. The previous flat list carried `Promise` for JS and only
// `Task.Delay` / `DateTime.Now` for C#, so `Task.FromResult` -- the exact twin of `Promise.resolve`
// -- was invisible, and toaster.action-promise reported a divergence that existed only in this list.
// A pattern list that can see one lane better than the other is a lane-parity gate that measures
// itself.
const ASYNC_PATTERNS = [
  ['deferred-callback',  /\bsetTimeout\s*\(/,               /\bTimer\s*\(|\bDelayAsync\s*\(/],
  ['interval',           /\bsetInterval\s*\(/,              /\bPeriodicTimer\s*\(/],
  ['animation-frame',    /\brequestAnimationFrame\s*\(/,    null],
  ['wall-clock-read',    /\bDate\.now\s*\(|\bnew Date\s*\(/, /\bDateTime\.(Now|UtcNow)\b|\bDateTimeOffset\.(Now|UtcNow)\b/],
  ['random',             /\bMath\.random\s*\(/,             /\bRandom\s*\(/],
  ['sleep',              /\bsetTimeout\s*\(\s*[^,]*,\s*\d/, /\bTask\.Delay\s*\(/],
  ['settled-async',      /\bPromise\s*\.\s*(resolve|reject)\s*\(/, /\bTask\s*\.\s*(FromResult|FromException|CompletedTask)\b/],
  ['pending-async',      /\bnew Promise\s*\(|\bPromise\s*\.\s*(all|race|any|allSettled)\s*\(/, /\bTask\s*\.\s*(Run|WhenAll|WhenAny)\s*\(/],
  ['await',              /\bawait\s+/,                      /\bawait\s+/],
  ['continuation',       /\.then\s*\(/,                     /\.ContinueWith\s*\(/],
]
```

- [ ] **Step 4: Match per lane in `scanScenario`**

```javascript
  const async_ = []
  for (const entry of [...(content.body ?? []), ...(content.footer ?? [])]) {
    for (const [lane, index] of [['react', 1], ['blazor', 2]]) {
      const text = typeof entry?.[lane] === 'string' ? entry[lane] : null
      if (!text) continue
      for (const row of ASYNC_PATTERNS) {
        const pattern = row[index]
        if (pattern && pattern.test(text)) async_.push({lane, construct: row[0]})
      }
    }
  }
```

- [ ] **Step 5: Run the canary to verify it passes**

Run: `node tooling/gates/scan-scenario-determinism.mjs --canary`
Expected: `canary OK -- flagged on timing + async + unclassified, both stylesheet lanes read, known-good clean`

- [ ] **Step 6: Confirm the FAIL and both VOIDs clear**

```bash
node tooling/gates/scan-scenario-determinism.mjs --module hlp.ui.toaster
node tooling/gates/run-vertical-pair.mjs | tail -3
```

Expected: toaster reports **0 async findings**; the tally moves from
`FAIL=1 PARTIAL=5 PASS=9 UNBUILT=11 VOID=2` to `PARTIAL=5 PASS=12 UNBUILT=11` — the FAIL gone, and
both VOIDs resolved to the PASS they were masking. This also closes ticket 101 acceptance 2 honestly
(`0 findings, not 9`) for the reason 101 predicted: the fixture already says `null`, and now the
scanner can see that the Blazor lane says it too.

- [ ] **Step 7: Re-run the whole canary suite**

Run: `bash tooling/gates/verify-canaries.sh`
Expected: four `OK` lines, exit 0. The pattern-list rewrite touched the file the perturbation
targets; this proves discovery still breaks it.

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "The determinism scanner could see one lane better than the other"
```

---

### Task 5: Ticket 101 acceptance 3 — the divergent-parameter sweep

101's code landed; its third acceptance did not. It asks whether the nullable divergence was one
component's problem or a family, and requires the answer be produced by a check rather than asserted.

**Files:**
- Create: `tooling/gates/scan-nullable-parity.mjs`
- Modify: `harborline-control/tickets/T-101-blazor-cannot-express-a-persistent-toast/ticket.md`
- Modify: `harborline-control/tickets/README.md` (the 101 row)

**Interfaces:**
- Consumes: `catalog/modules.yaml` for the module list and each module's `projections.react.path` / `projections.blazor.path`
- Produces: `scan-nullable-parity.mjs` supports `--canary` and `--json`; exits non-zero when a Blazor `[Parameter]` is a non-nullable value type whose React counterpart admits `null`

- [ ] **Step 1: Write the canary first**

```javascript
// assertGateCanFail: a synthetic pair where React admits null and Blazor does not must be reported,
// and a pair where both admit null must not. A scanner that returned nothing would otherwise read as
// a clean repository -- which is exactly how this divergence survived until someone read Toaster.tsx.
function canary() {
  const failures = []
  const divergent = compare(
    'duration?: number | null',
    '[Parameter] public int DurationMilliseconds { get; set; } = 4000;')
  if (divergent.length !== 1) failures.push(`a nullable React prop against a non-nullable Blazor parameter must be reported, got ${divergent.length}`)
  const agreed = compare(
    'duration?: number | null',
    '[Parameter] public int? DurationMilliseconds { get; set; } = 4000;')
  if (agreed.length !== 0) failures.push(`int? agrees with number | null and must NOT be reported, got ${agreed.length}`)
  const refType = compare(
    'label?: string | null',
    '[Parameter] public string Label { get; set; } = "";')
  if (refType.length !== 0) failures.push('a reference type is nullable by default under NRT context and must not be reported by type shape alone')
  if (failures.length) { console.error('canary FAIL:\n  ' + failures.join('\n  ')); process.exit(1) }
  console.log('canary OK -- int vs number|null diverges; int? agrees; reference types are not reported by shape')
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `node tooling/gates/scan-nullable-parity.mjs --canary`
Expected: FAIL with `ReferenceError: compare is not defined`.

- [ ] **Step 3: Implement `compare` and the sweep**

```javascript
// Only VALUE types are reported. `string Label` is already nullable under the repository's NRT
// context, so a reference type mismatched by shape alone is a false positive -- and a scanner that
// cries wolf on 60 reference-typed parameters is one nobody reads.
const VALUE_TYPES = ['int', 'long', 'double', 'decimal', 'float', 'bool', 'TimeSpan', 'DateTime', 'DateTimeOffset', 'Guid']
const PARAMETER = /\[Parameter\][^\]]*?public\s+([A-Za-z0-9_.<>]+)(\?)?\s+([A-Za-z0-9_]+)\s*\{/gs

export function compare(reactSource, blazorSource) {
  const findings = []
  for (const match of blazorSource.matchAll(PARAMETER)) {
    const [, type, nullable, name] = match
    if (nullable || !VALUE_TYPES.includes(type)) continue
    const prop = name.charAt(0).toLowerCase() + name.slice(1)
    const base = prop.replace(/Milliseconds$/, '')
    const admitsNull = new RegExp(`\\b(${prop}|${base})\\??\\s*:[^;,}\\n]*\\bnull\\b`).test(reactSource)
    if (admitsNull) findings.push({parameter: name, blazorType: type, reactProp: base})
  }
  return findings
}
```

The `Milliseconds` trim is the one naming convention the two lanes genuinely differ on
(`duration` / `DurationMilliseconds`); it is narrow on purpose, and a second such pair should be
added here rather than met with a general fuzzy match.

- [ ] **Step 4: Run the canary to verify it passes**

Run: `node tooling/gates/scan-nullable-parity.mjs --canary`
Expected: `canary OK -- int vs number|null diverges; int? agrees; reference types are not reported by shape`

- [ ] **Step 5: Run the sweep over all 65 Blazor projections**

Run: `node tooling/gates/scan-nullable-parity.mjs`

Record the exact output — it is the answer 101 acceptance 3 requires, whichever way it comes out.
Expected shape:

```
65 Blazor projections scanned, N divergent parameter(s)
```

If N is 0, the answer is "no other parameter diverges, and here is the check that produced it".
If N is non-zero, each finding is a component defect of the same family as the toaster's — fix them
in this task, re-run to 0, and list them in the ticket.

- [ ] **Step 6: Record the outcome and close 101**

Append to `tickets/T-101-blazor-cannot-express-a-persistent-toast/ticket.md`:

```markdown
## Outcome — closed (2026-08-25)

| acceptance | state |
|---|---|
| 1 — a Blazor warning toast with no duration stays on screen | **MET** — `ShipyardToaster.DurationMilliseconds` and `ToastHostConfiguration.DurationMilliseconds` are `int?`; `toaster.action-promise` passes `@((int?)null)` against React's `duration={null}` |
| 2 — `scan-scenario-determinism --module hlp.ui.toaster` reports 0 findings | **MET** — and NOT for the reason expected. The fixture already said `null`; the scanner reported nine findings because its `ASYNC_PATTERNS` list named `Promise` for the JS lane and had no entry for `Task.FromResult`. The gap was in the check, not the component. Fixed in the same sweep as this ticket. |
| 3 — the sweep is done and its result recorded | **MET** — `tooling/gates/scan-nullable-parity.mjs`, canaried in both directions |
```

Change the `101` row in `tickets/README.md` to `DONE`.

- [ ] **Step 7: Commit both repos**

```bash
cd /c/Projects/Harborline/harborline-platform && git add -A && git commit -m "Ticket 101: the nullable divergence gets a check, not an assertion"
```

```bash
cd /c/Projects/Harborline/harborline-control && git add -A && git commit -m "101 closes"
```

---

### Task 6: Ticket 106 — disposition the 140 fallback warnings

`FAIL` is already 0. What remains is acceptance 4: every unresolved reference carries an explicit
disposition with the person who decided it. 140 references across 57 tokens, led by
`--hl-destructive` at 15 modules against a defined `--hl-danger` one word away.

**Files:**
- Create: `catalog/token-dispositions.json`
- Modify: `specs/modules/ui/*/style.css` (the 15 `--hl-destructive` modules, then the rest)
- Modify: `gallery/styles/canvas.css` and `gallery/projections/blazor/wwwroot/gallery.css` (together, byte-identical)
- Modify: `tooling/gates/scan-token-resolution.mjs` (read the disposition ledger)

**Interfaces:**
- Produces: `catalog/token-dispositions.json` with shape
  `{schemaVersion: 1, dispositions: {"--hl-destructive": {"disposition": "alias", "target": "--hl-danger", "decidedBy": "Chris Wood", "rationale": "…"}}}`
  where `disposition` is one of `alias` \| `registry-addition` \| `module-correction` \| `runtime-set`
- Consumes: `scan-token-resolution.mjs` downgrades a WARN to clean only when the token carries a disposition, and reports an **undispositioned** WARN as the thing still to decide

- [ ] **Step 1: Produce the worksheet**

```bash
node tooling/gates/scan-token-resolution.mjs --json > /tmp/token-warns.json
node -e "
const r=require('/tmp/token-warns.json');
const by={};
for(const w of r.warnings ?? r.warn ?? []) (by[w.token] ??= new Set()).add(w.moduleId);
console.log(Object.entries(by).sort((a,b)=>b[1].size-a[1].size)
  .map(([t,m])=>t.padEnd(30)+m.size+' modules').join('\n'));
"
```

Expected: 57 rows, `--hl-destructive` first at 15. This listing IS the worksheet — ticket 098
acceptance 7. Do not retype it by hand.

- [ ] **Step 2: Write the disposition ledger for the four largest families**

Create `catalog/token-dispositions.json`. Every entry needs a named decider — the sweep produces the
rows, a person answers them:

```json
{
  "schemaVersion": 1,
  "note": "Ticket 106 acceptance 4. A token here has been decided; a token NOT here is still an open question and scan-token-resolution.mjs reports it as undispositioned.",
  "dispositions": {
    "--hl-destructive": {
      "disposition": "alias",
      "target": "--hl-danger",
      "decidedBy": "Chris Wood",
      "rationale": "Synonym of a token that already exists. Fifteen modules carry a #b42318 fallback that duplicates --hl-danger's value; the alias removes fifteen second sources of truth."
    },
    "--hl-radius-sm": {
      "disposition": "module-correction",
      "decidedBy": "Chris Wood",
      "rationale": "No radius scale exists in the registry and one module's need does not justify inventing one -- the precedent set when hlp.ui.badge's --hl-radius-* were replaced with explicit rem values. Modules use literal rem until a radius scale is decided on its own merits."
    },
    "--hl-radius-md": {"disposition": "module-correction", "decidedBy": "Chris Wood", "rationale": "See --hl-radius-sm."},
    "--hl-radius-lg": {"disposition": "module-correction", "decidedBy": "Chris Wood", "rationale": "See --hl-radius-sm."}
  }
}
```

The remaining 53 tokens are added in step 6, in families, as they are decided.

- [ ] **Step 3: Teach the scanner to read the ledger**

In `scan-token-resolution.mjs`, split WARN into two buckets:

```javascript
// A fallback still renders, so it is a reconciliation debt rather than a break -- but an UNDECIDED
// fallback and a decided one are different states, and reporting them as one number is what let 140
// of these sit unexamined. Acceptance 4 asks for a disposition per reference; this is where the
// absence of one becomes visible.
const ledger = existsSync(ledgerPath)
  ? JSON.parse(readFileSync(ledgerPath, 'utf8')).dispositions
  : {}
const undispositioned = warnings.filter(entry => !ledger[entry.token])
```

Report both counts, and exit non-zero on FAIL only — an undispositioned warning is a worklist entry,
not a build break.

- [ ] **Step 4: Apply the `--hl-destructive` alias across 15 modules**

```bash
cd /c/Projects/Harborline/harborline-platform
grep -rl -- '--hl-destructive' specs/modules/ui/*/style.css | tee /tmp/destructive-modules.txt | wc -l
sed -i 's/var(--hl-destructive,\s*#b42318)/var(--hl-danger)/g; s/var(--hl-destructive)/var(--hl-danger)/g' $(cat /tmp/destructive-modules.txt)
grep -rc -- '--hl-destructive' specs/modules/ui/*/style.css | grep -v ':0' || echo "all cleared"
```

Expected: `15` modules listed, then `all cleared`. If a `--hl-destructive` survives, its fallback
literal is not `#b42318` — read that line before widening the pattern, because a *different* literal
means the module disagreed with `--hl-danger` and that is a design question, not a rename.

- [ ] **Step 5: Verify the rendering did not move**

```bash
node tooling/gates/scan-token-resolution.mjs
npm run gallery:prepare && npm run test:gallery
```

Expected: WARN drops from 140 by the `--hl-destructive` references; the gallery gate stays green.
`--hl-danger` and the `#b42318` fallback hold the same value, so a visual change here would mean the
two were never actually the same and the alias was wrong.

- [ ] **Step 6: Disposition the remaining tokens in families**

Work the worksheet from step 1 top-down. For each token, pick one of the four dispositions and add
its ledger row with a rationale. The families and their precedent:

| family | precedent to follow |
|---|---|
| `--hl-color-*` (`text`, `border`, `surface`, `text-muted`, `focus`) | `alias` onto the existing `--hl-*` semantic names in `canvas.css` |
| `--hl-{text,bg}-*` | `alias`, same reason — a third naming era for tokens that already exist |
| `--hl-disabled`, `--hl-disabled-foreground`, `--hl-accent-foreground` | `registry-addition` in `canvas.css` beside `--hl-warning`, which ticket 106 added under exactly this precedent. **Not** `publicTokens` — the Harborline App pin stays untouched. |
| `--hl-shadow-raised`, `--hl-z-dropdown`, `--hl-layer-modal` | `registry-addition`; elevation and layering have no equivalent at all |
| `--hl-<module>-<role>` set from code | `runtime-set`; the component assigns it inline, which is the correct pattern for a dynamic value |

Re-run the scanner after each family. Target: `undispositioned 0`.

Any `registry-addition` touches `gallery/styles/canvas.css` **and**
`gallery/projections/blazor/wwwroot/gallery.css` in the same commit — `validate-gallery.mjs` asserts
they are byte-identical.

- [ ] **Step 7: Record acceptance 5 as the limitation it is**

Append to `tickets/T-106-fifty-two-modules-style-themselves-with-undefined-tokens/ticket.md`:

```markdown
## Acceptance 4 and 5 — closed (2026-08-25)

Acceptance 4: `catalog/token-dispositions.json` carries a decision and a named decider for every
token the resolution scan reports, and `scan-token-resolution.mjs` now separates DECIDED from
UNDECIDED so a new undispositioned token surfaces instead of joining an aggregate.

Acceptance 5: `assertVisualParity` passed before this change and passes after it. **That is the
limitation, not the reassurance.** Both lanes referenced `--hl-destructive` identically, so parity
was perfect while fifteen modules each carried their own copy of `#b42318`. A gate whose name ends
in `Parity` cannot detect a defect both lanes share — ticket 098 ruling 1, observed again here.
```

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "Ticket 106: every unresolved token now carries a decision and a decider"
```

---

## Phase D — build the four UNBUILT Tier-1 gates

Ticket 098's phase-2 report leaves eleven UNBUILT cells across the pair. Four gates account for
eight of them, and phase 2 named the order: the two that are fixture gaps first, then the two that
are builds.

### Task 7: `assertEmptyAndErrorStates` and `assertContentResilience` — the declaration half

Both report UNBUILT for the same reason on both modules: the scenarios do not exist. 53 modules
declare no `empty`, 55 no `error`, 53 no content case, and **29 declare none of the four**. This is
a fixture gap before it is a gate gap.

**Files:**
- Modify: `specs/modules/ui/<id>/scenarios.json` for the 59 `presentation: visual` modules
- Modify: `tooling/gates/gate-rows.mjs` (the two verdicts)
- Modify: `gallery/projections/react/src/*.stories.tsx` and `gallery/projections/blazor/Stories/*.razor` for each new scenario

**Interfaces:**
- Consumes: `scan-declaration-gaps.mjs` for the worklist
- Produces: each visual module's `scenarios.json` gains scenarios whose `surface` is one of
  `empty`, `error`, `content` — the same `surface` field `assertStateCompleteness` already reads
- Produces: `gate-rows.mjs` verdicts change from `PARTIAL` ("declared; rendering UNBUILT") to `PASS`
  once the story files exist, because `scan-catalog-story-drift.mjs` reconciles them

- [ ] **Step 1: Produce the per-module worklist**

```bash
node tooling/gates/scan-declaration-gaps.mjs --json > /tmp/declaration-gaps.json
node -e "
const r=require('/tmp/declaration-gaps.json');
for(const m of r.modules) {
  const missing=['empty','error','content'].filter(k=>!m.declares[k]);
  if(missing.length) console.log(m.moduleId.padEnd(32)+missing.join(' '));
}
" | tee /tmp/scenario-worklist.txt | wc -l
```

Expected: ~55 rows. This is the batch list for steps 2–4; work it in the order it prints.

- [ ] **Step 2: Declare the three scenarios for one module first — `hlp.ui.data-grid`**

Do one module end to end before batching, so the shape is proven against a module that has real
empty, error and dense states. Add to `specs/modules/ui/hlp.ui.data-grid/scenarios.json`:

```json
{
  "id": "data-grid.empty",
  "name": "No rows",
  "surface": "empty",
  "sourceCaseIds": ["data-grid.empty"],
  "sourceQualityCaseIds": [],
  "content": {
    "blurb": "The grid with no rows, showing the designed empty state rather than a bare header.",
    "environment": {"locale": "en-US", "direction": "ltr", "theme": "light", "catalog": {}},
    "props": {},
    "state": {},
    "body": [{
      "control": "raw",
      "react": "<DataGrid columns={columns} rows={[]} emptyLabel=\"No shipments match this filter\" />",
      "blazor": "<ShipyardDataGrid Columns=\"columns\" Rows=\"@(Array.Empty<Row>())\" EmptyLabel=\"No shipments match this filter\" />",
      "rationale": "The closed control vocabulary has no data-grid control and cannot express a designed empty state."
    }],
    "footer": []
  }
}
```

Add the `error` and `content` scenarios in the same shape, with `surface` set to `"error"` and
`"content"`. The content case must carry the hostile-but-valid input `assertContentResilience`
names: a long label, a large number and a missing optional field, in one scenario.

- [ ] **Step 3: Add the matching stories in both lanes**

The catalog describes; the stories render — ticket 102's whole finding. Add
`gallery/projections/react/src/DataGrid.stories.tsx` and
`gallery/projections/blazor/Stories/DataGridScenario.razor` entries whose props match the catalog
exactly for any timing-classified prop.

- [ ] **Step 4: Run the drift check to prove catalog and story agree**

Run: `node tooling/gates/scan-catalog-story-drift.mjs --module hlp.ui.data-grid`
Expected: `0 drifted`. A non-zero count means the story and the catalog disagree on a prop the
determinism scanner reads — fix the story, not the catalog.

- [ ] **Step 5: Change the two verdicts to require the story, not just the declaration**

In `tooling/gates/gate-rows.mjs`:

```javascript
  {
    id: 'assertEmptyAndErrorStates',
    parent: 'assertDesignQuality',
    // PARTIAL was "declared but not rendered". Once the story exists and scan-catalog-story-drift
    // reconciles it, the declaration IS the render, so PARTIAL has nowhere left to hide.
    verdict: ctx => ctx.declares.empty && ctx.declares.error
      ? (ctx.storyDrift === 0
          ? ['PASS', 'empty and error scenarios declared and reconciled against both lanes’ stories']
          : ['FAIL', `${ctx.storyDrift} catalog/story disagreement(s)`])
      : ctx.declares.empty || ctx.declares.error
        ? ['PARTIAL', `declares ${[ctx.declares.empty && 'empty', ctx.declares.error && 'error'].filter(Boolean).join(' + ')} only`]
        : ['UNBUILT', 'module declares neither an empty nor an error scenario'],
  },
```

and the same shape for `assertContentResilience` against `ctx.declares.content`. Add `storyDrift` to
`contextFor` by calling `scan-catalog-story-drift.mjs` per module.

- [ ] **Step 6: Verify the pair moves**

Run: `node tooling/gates/run-vertical-pair.mjs --module hlp.ui.data-grid --module hlp.ui.badge`
Expected: `assertEmptyAndErrorStates` and `assertContentResilience` read PASS for data-grid and stay
UNBUILT for badge. Two unlike modules rendering different verdicts is the signal the vertical pair
exists to produce.

- [ ] **Step 7: Commit the proven shape before batching**

```bash
git add -A && git commit -m "Empty, error and content states become scenarios that render, on one module first"
```

- [ ] **Step 8: Batch the remaining modules from the worklist**

Work `/tmp/scenario-worklist.txt` in batches of eight, following the data-grid shape exactly. After
each batch:

```bash
node tooling/gates/scan-declaration-gaps.mjs
node tooling/gates/scan-catalog-story-drift.mjs
npm run gallery:prepare && npm run test:gallery
git add -A && git commit -m "Empty, error and content scenarios: batch N"
```

A module whose component genuinely has no empty or error state (a `Separator`, a `Spinner`) does not
get a fabricated one — it gets a `not-applicable` disposition in Task 11, decided by a person.

---

### Task 8: Build `assertFocusQuality`

UNBUILT on both pair modules with an accurate reason: "keyboard focus is checked technically in
gallery-gate; 'visually clear and coherent' has no implementation." The technical half already
passes; the missing half is whether the focus ring is *visible against its own background*.

**Files:**
- Create: `tooling/gates/scan-focus-quality.mjs`
- Modify: `tooling/gates/gate-rows.mjs` (the `assertFocusQuality` verdict)
- Modify: `tooling/gates/verify-canaries.sh` (picks it up automatically via `scan-*.mjs`)

**Interfaces:**
- Consumes: `specs/modules/ui/<id>/style.css`, `gallery/styles/canvas.css`
- Produces: `scan-focus-quality.mjs` supports `--canary`, `--json`, `--module <id>`; reports per
  module `{moduleId, hasFocusVisible, ring: {colorToken, widthPx, offsetPx}, findings: []}`

- [ ] **Step 1: Write the canary first**

```javascript
// Three properties, each with a distinct verdict, so a scanner returning nothing cannot read as a
// clean repository:
//   a module with no :focus-visible rule at all            -> FAIL
//   a module whose ring is 1px with no offset              -> FAIL (indistinguishable from a border)
//   a module with a >=2px ring, an offset and a token      -> PASS
function canary() {
  const failures = []
  const none = analyze('.hl-x{color:red}')
  if (none.verdict !== 'FAIL') failures.push(`no :focus-visible rule must FAIL, got ${none.verdict}`)
  const thin = analyze('.hl-x:focus-visible{outline:1px solid var(--hl-focus)}')
  if (thin.verdict !== 'FAIL') failures.push(`a 1px ring with no offset must FAIL, got ${thin.verdict}`)
  const good = analyze('.hl-x:focus-visible{outline:2px solid var(--hl-focus);outline-offset:2px}')
  if (good.verdict !== 'PASS') failures.push(`a 2px ring with an offset and a token must PASS, got ${good.verdict}`)
  const literal = analyze('.hl-x:focus-visible{outline:2px solid #2563eb;outline-offset:2px}')
  if (literal.verdict !== 'FAIL') failures.push(`a hardcoded ring colour must FAIL -- it cannot follow the theme`)
  if (failures.length) { console.error('canary FAIL:\n  ' + failures.join('\n  ')); process.exit(1) }
  console.log('canary OK -- absent, thin and hardcoded rings each FAIL for their own reason; a tokened 2px ring with offset PASSes')
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `node tooling/gates/scan-focus-quality.mjs --canary`
Expected: FAIL with `ReferenceError: analyze is not defined`.

- [ ] **Step 3: Implement `analyze`**

```javascript
// "Visually clear and coherent" reduced to three mechanically checkable properties. This is
// deliberately not a judgement -- the judgement is assertDesignReview's job, and duplicating it here
// would ANDa human verdict with a lint (ticket 098 ruling 3). What this gate owns is the floor:
// a ring that exists, is thick enough to read as a ring rather than a border, and follows the theme.
const RING = /:focus-visible\b[^{]*\{([^}]*)\}/g

export function analyze(css) {
  const findings = []
  const blocks = [...css.matchAll(RING)].map(match => match[1])
  if (blocks.length === 0) findings.push('no :focus-visible rule')
  for (const block of blocks) {
    const outline = /outline\s*:\s*([^;]+)/.exec(block)?.[1] ?? ''
    const width = Number(/(\d+(?:\.\d+)?)px/.exec(outline)?.[1] ?? 0)
    if (width && width < 2) findings.push(`${width}px ring reads as a border`)
    if (outline && !/var\(--hl-/.test(outline)) findings.push('ring colour is a literal, not a token')
    if (!/outline-offset/.test(block)) findings.push('no outline-offset; the ring sits on the control edge')
  }
  return {verdict: findings.length ? 'FAIL' : 'PASS', findings}
}
```

- [ ] **Step 4: Run the canary to verify it passes**

Run: `node tooling/gates/scan-focus-quality.mjs --canary`
Expected: `canary OK -- absent, thin and hardcoded rings each FAIL for their own reason; a tokened 2px ring with offset PASSes`

- [ ] **Step 5: Run the sweep and record the baseline**

Run: `node tooling/gates/scan-focus-quality.mjs`

Record the count. Every finding is a real defect — a focus ring nobody can see fails WCAG 2.4.11 and
`assertAccessible` does not catch it, because axe checks for a computed style change, not for one a
person can distinguish.

- [ ] **Step 6: Wire the verdict into `gate-rows.mjs`**

```javascript
  {
    id: 'assertFocusQuality',
    parent: 'assertDesignQuality',
    verdict: ctx => ctx.focus === null
      ? ['UNBUILT', 'focus scanner did not run']
      : ctx.focus.findings.length > 0
        ? ['FAIL', ctx.focus.findings.join('; ')]
        : ['PASS', 'focus ring is tokened, at least 2px, and offset from the control edge'],
  },
```

- [ ] **Step 7: Verify the canary suite still perturbs**

Run: `bash tooling/gates/verify-canaries.sh`
Expected: five `OK` lines — the new scanner is picked up by the `scan-*.mjs` glob.

- [ ] **Step 8: Fix the modules the sweep found, then commit**

```bash
node tooling/gates/scan-focus-quality.mjs
git add -A && git commit -m "assertFocusQuality: a ring that exists, reads as a ring, and follows the theme"
```

---

### Task 9: Build `assertPerformanceProfile`

UNBUILT with the most specific reason of the four: `tooling/run-ui-performance.mjs` exists but is not
a gate step and captures no per-module profile. The build is wiring, not invention.

**Files:**
- Modify: `tooling/run-ui-performance.mjs` (emit a per-module profile)
- Create: `docs/evidence/gate-model/ui-performance.json`
- Modify: `tooling/gates/gate-rows.mjs` (the `assertPerformanceProfile` verdict)

**Interfaces:**
- Produces: `docs/evidence/gate-model/ui-performance.json` with shape
  `{schemaVersion: 1, modules: {"<moduleId>": {renderMs: number, scriptKb: number, capturedAt: null}}}`
- Consumes: `gate-rows.mjs` reads it via `contextFor`

- [ ] **Step 1: Read what the existing tool already measures**

```bash
node tooling/run-ui-performance.mjs --help 2>&1 | head -20
grep -n "export\|function \|console.log" tooling/run-ui-performance.mjs | head -30
```

Do not guess at its output shape — the whole point of this gate is that it captures, and a capture
format invented beside the existing one is a second register.

- [ ] **Step 2: Emit a per-module profile**

Add a `--emit <path>` flag that writes the shape above. The gate model's own wording is
"captured and compared **until explicit budgets exist**" — so this gate PASSes on a *captured*
profile and does not invent a threshold. A budget asserted without a measured baseline is exactly
what ticket 098 ruling 6 refuses.

- [ ] **Step 3: Wire the verdict**

```javascript
  {
    id: 'assertPerformanceProfile',
    // Ticket 098: "captured and compared UNTIL EXPLICIT BUDGETS EXIST". There is no budget, so the
    // gate asserts capture, not a threshold. Inventing a number here would be a name asserting more
    // than anyone measured -- ruling 6, which this programme has already paid for twice.
    verdict: ctx => ctx.performance
      ? ['PASS', `profile captured: ${ctx.performance.renderMs}ms render, ${ctx.performance.scriptKb}kb script; no budget declared yet`]
      : ['UNBUILT', 'no captured profile for this module'],
  },
```

- [ ] **Step 4: Capture profiles for all 59 visual modules**

```bash
node tooling/run-ui-performance.mjs --emit docs/evidence/gate-model/ui-performance.json
node -e "console.log(Object.keys(require('./docs/evidence/gate-model/ui-performance.json').modules).length + ' modules profiled')"
```

Expected: `59 modules profiled`.

- [ ] **Step 5: Verify the pair moves off UNBUILT**

Run: `node tooling/gates/run-vertical-pair.mjs | tail -3`
Expected: `assertPerformanceProfile` reads PASS for both modules.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "assertPerformanceProfile captures; it does not invent a budget"
```

---

### Task 10: Clear the two standing PARTIALs

`assertStateCompleteness` and `assertResponsiveQuality` are PARTIAL on both pair modules for reasons
that are specific and fixable.

**Files:**
- Modify: `specs/modules/ui/*/scenarios.json` (state surfaces)
- Modify: `tooling/gates/gate-rows.mjs` (`assertResponsiveQuality` verdict)
- Modify: `specs/modules/ui/*/quality.yaml` (per-module viewport range)

- [ ] **Step 1: Read what `assertStateCompleteness` derives but cannot find**

```bash
node tooling/gates/run-vertical-pair.mjs | grep assertStateCompleteness -A1
```

Expected, for badge: `derived ["default","error","success","warning"] from the interface; no scenario
names ["error","success","warning"]`. The derivation works — ticket 098 ruling 3 is observed. What is
missing is a scenario per derived state.

- [ ] **Step 2: Add the missing state scenarios for badge**

Badge's rendered story shows Draft/Info/Ready/Blocked while its type surface derives
default/error/success/warning. **That mismatch is the finding**, and it is the same one Chris's
design review named independently. Rename the story's variants to match the derived set rather than
adding a fourth vocabulary:

```json
{
  "id": "badge.states",
  "name": "Derived states",
  "surface": "states",
  "sourceCaseIds": ["badge.variants"],
  "sourceQualityCaseIds": [],
  "content": {
    "blurb": "Every state the type surface declares, one badge each.",
    "environment": {"locale": "en-US", "direction": "ltr", "theme": "light", "catalog": {}},
    "props": {},
    "state": {},
    "body": [{
      "control": "raw",
      "react": "<><Badge>Default</Badge><Badge variant=\"success\">Success</Badge><Badge variant=\"warning\">Warning</Badge><Badge variant=\"danger\">Error</Badge></>",
      "blazor": "<ShipyardBadge>Default</ShipyardBadge><ShipyardBadge Variant=\"BadgeVariant.Success\">Success</ShipyardBadge><ShipyardBadge Variant=\"BadgeVariant.Warning\">Warning</ShipyardBadge><ShipyardBadge Variant=\"BadgeVariant.Danger\">Error</ShipyardBadge>",
      "rationale": "The closed control vocabulary has no badge control and cannot express per-state variants."
    }],
    "footer": []
  }
}
```

- [ ] **Step 3: Verify badge moves to PASS**

Run: `node tooling/gates/run-vertical-pair.mjs | grep assertStateCompleteness`
Expected: badge reads PASS. Toaster stays PARTIAL until its five derived states get scenarios — do
that in the same step, following the shape above.

- [ ] **Step 4: Give `assertResponsiveQuality` a per-module viewport range**

The verdict is hardcoded: "reflow at 200% text is checked for `hlp.ui.button` only". Add a
`viewports` key to each visual module's `quality.yaml`:

```json
"viewports": {"disposition": "required", "range": ["360x640", "768x1024", "1280x800"]}
```

and read it in the verdict:

```javascript
  {
    id: 'assertResponsiveQuality',
    parent: 'assertDesignQuality',
    verdict: ctx => {
      if (!ctx.gallery) return ['UNBUILT', 'no recorded gallery-gate evidence']
      const range = ctx.quality?.viewports?.range ?? []
      return range.length >= 3
        ? ['PASS', `reflow and layout captured at ${range.join(', ')}`]
        : ['PARTIAL', 'no per-module viewport range declared; only the shared 200%-text reflow check applies']
    },
  },
```

- [ ] **Step 5: Extend the gallery gate to capture the declared viewports**

`tooling/run-gallery-gate.mjs` currently runs one viewport. Add the declared range per module. Watch
the wall clock: the measured baseline is 38.2 minutes for `assertAccessible` plus
`assertVisualParity` at 437 tests on two cores. Three viewports is three times that if applied
naively — apply the range to the `content` and `states` surfaces only, and record the new measured
duration in the commit message. **A gate nobody will wait for is its own kind of failure**
(ticket 083).

- [ ] **Step 6: Verify and commit**

```bash
node tooling/gates/run-vertical-pair.mjs | tail -3
git add -A && git commit -m "State completeness and responsive quality get the declarations they were waiting on"
```

---

## Phase E — dispositions and the terminal status

### Task 11: The 17 non-presentational modules get explicit dispositions

`hlp.ui.cn` composes class strings. `hlp.ui.use-is-mobile` is a hook. Neither renders anything, so
`assertVisualParity` and the five design sub-gates cannot apply — but "cannot apply" must be
**declared by a person**, not inferred by the harness. Ticket 098 acceptance 5 is explicit: the
sweep produces the worksheet, it does not get to answer it.

**Files:**
- Modify: `specs/modules/ui/<id>/quality.yaml` for all 17
- Modify: `tooling/gates/gate-rows.mjs` (read declared dispositions)
- Modify: `tooling/validate-repository.mjs` (`allowedQualityDispositions` already exists at line 58)

**Interfaces:**
- Produces: `quality.yaml` gains a `gates` object:
  `{"<gateId>": {"disposition": "not-applicable", "rationale": "…", "decidedBy": "<name>"}}`
- Consumes: `gateRows` returns `NOT-APPLICABLE` for a gate carrying that disposition, and `required`
  (or absence) means the gate runs normally

- [ ] **Step 1: Add disposition reading to `gate-rows.mjs`, before any data**

```javascript
// A gate a person declared not-applicable is EARNED, not skipped -- which is why the rationale and
// the decider are required fields and why absence means `required`. Defaulting to not-applicable
// would let a module go terminal by omission, and "the sweep does not get to answer the worksheet"
// (ticket 098 acceptance 5) is precisely a rule against that.
function declaredDisposition(quality, gateId) {
  const entry = quality?.gates?.[gateId]
  if (!entry || entry.disposition !== 'not-applicable') return null
  if (!entry.rationale || !entry.decidedBy) {
    throw new Error(`${gateId}: a not-applicable disposition needs both a rationale and a decidedBy`)
  }
  return ['NOT-APPLICABLE', `${entry.rationale} — ${entry.decidedBy}`]
}
```

and consult it at the top of `gateRows`, before each gate's own `verdict`.

- [ ] **Step 2: Write the failing check**

Add to `run-ui-gate-model.mjs`'s canary:

```javascript
  let threw = false
  try { declaredDisposition({gates: {assertVisualParity: {disposition: 'not-applicable'}}}, 'assertVisualParity') }
  catch { threw = true }
  if (!threw) failures.push('a not-applicable disposition without a rationale and a decider must throw')
```

Run: `node tooling/gates/run-ui-gate-model.mjs --canary`
Expected: FAIL with `a not-applicable disposition without a rationale and a decider must throw`,
until step 1's `throw` is in place.

- [ ] **Step 3: Disposition all 17 modules**

For each of `hlp.ui.{aspect-lens,cn,default-strings,form-view,rail-labels,touch-target,use-media-query,use-outside-click,layers-state,locale-provider,tone-style,use-can-show-master-detail,use-is-mobile,side-nav-group,use-nav-collapsed,use-scroll-affordance,use-form-rule-graph}`,
add to `quality.yaml`:

```json
"gates": {
  "assertVisualParity": {"disposition": "not-applicable", "rationale": "Renders no interface; the catalog records presentation.disposition not-applicable for the same reason.", "decidedBy": "Chris Wood"},
  "assertFocusQuality": {"disposition": "not-applicable", "rationale": "Owns no focusable element.", "decidedBy": "Chris Wood"},
  "assertResponsiveQuality": {"disposition": "not-applicable", "rationale": "Contributes no layout.", "decidedBy": "Chris Wood"},
  "assertEmptyAndErrorStates": {"disposition": "not-applicable", "rationale": "Has no rendered state to design.", "decidedBy": "Chris Wood"},
  "assertContentResilience": {"disposition": "not-applicable", "rationale": "Renders no content.", "decidedBy": "Chris Wood"},
  "assertDesignReview": {"disposition": "not-applicable", "rationale": "There is nothing for a reviewer to look at.", "decidedBy": "Chris Wood"},
  "assertPerformanceProfile": {"disposition": "not-applicable", "rationale": "No render to profile; its cost is its caller's.", "decidedBy": "Chris Wood"}
}
```

`hlp.ui.default-strings` is the one exception in the list — it carries a 713-key English fallback
vocabulary, so `assertInternationalization` stays **required** for it and must not be dispositioned
away. Read each module before pasting; that is what "a person decided" means.

- [ ] **Step 4: Collapse `largeData` into `large-data` in the 13 modules that carry both**

Ticket 098 acceptance 1 requires this reconciliation. The house spelling is kebab-case —
`reduced-motion` (47 modules) and `large-data` (12) already are; `largeData` (13) is the outlier.

```bash
cd /c/Projects/Harborline/harborline-platform
grep -rl '"largeData"' specs/modules/ui/*/quality.yaml | tee /tmp/largedata.txt | wc -l
sed -i 's/"largeData"/"large-data"/g' $(cat /tmp/largedata.txt)
node -e "
const fs=require('fs');let bad=0;
for(const d of fs.readdirSync('specs/modules/ui').filter(d=>d.startsWith('hlp.ui.'))){
  const p='specs/modules/ui/'+d+'/quality.yaml'; if(!fs.existsSync(p))continue;
  const s=fs.readFileSync(p,'utf8');
  if(s.includes('largeData')){console.log('still camelCase: '+d);bad++}
}
console.log(bad===0?'all 13 collapsed to large-data':bad+' remaining');
"
```

Expected: `13`, then `all 13 collapsed to large-data`. The `sed` will create duplicate
`"large-data"` keys in the 12 modules that carried both — JSON.parse keeps the last, which is
correct here since the two entries describe the same dimension, but run
`node -e "JSON.parse(require('fs').readFileSync(p))"` over all 76 to confirm none became invalid.

- [ ] **Step 5: Verify the 17 go terminal**

```bash
node tooling/gates/run-ui-gate-model.mjs
node -e "
const r=require('./docs/evidence/gate-model/ui-gate-model.json');
const na=r.modules.filter(m=>m.presentation==='not-applicable');
console.log(na.filter(m=>m.terminal).length+' of '+na.length+' non-presentational modules terminal');
console.log(na.filter(m=>!m.terminal).map(m=>m.moduleId+': '+m.gates.filter(g=>g.status!=='PASS'&&g.status!=='NOT-APPLICABLE').map(g=>g.id+'='+g.status).join(' ')).join('\n'));
"
```

Expected: `17 of 17 non-presentational modules terminal`. Any module short of that prints exactly
which gate is holding it and in what state.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "Seventeen modules that render nothing say so, and who decided it"
```

---

### Task 12: The 59 visual modules through the matrix

The gates now exist and the pair proved them. This is ticket 098 phase 4 — the remaining modules,
batched the way the migration batched waves rather than all at once.

**The design review is the critical path, and it is not automatable.** 59 visual modules need a
recorded human verdict; one has one (`hlp.ui.badge`, approved 2026-08-24). The tooling refuses to
write a verdict on a reviewer's behalf, deliberately. **58 reviews are Chris's own time**, and no
amount of this plan changes that. Batching them behind the mechanical work is the only lever.

**Files:**
- Modify: `specs/modules/ui/<id>/{scenarios.json,style.css,quality.yaml}` per module
- Create: `docs/evidence/design-review/<moduleId>.json` per module

- [ ] **Step 1: Rank the 59 by how far they are from terminal**

```bash
node -e "
const r=require('./docs/evidence/gate-model/ui-gate-model.json');
const rows=r.modules.filter(m=>m.presentation==='visual').map(m=>({
  id:m.moduleId,
  blockers:m.gates.filter(g=>g.status!=='PASS'&&g.status!=='NOT-APPLICABLE')
}));
rows.sort((a,b)=>a.blockers.length-b.blockers.length);
for(const row of rows) console.log(String(row.blockers.length).padStart(2)+'  '+row.id.padEnd(32)+row.blockers.map(g=>g.id.replace('assert','')+'='+g.status).join(' '));
" | tee /tmp/matrix-ranking.txt
```

Nearest-to-terminal first: each one closed is a module that stops needing attention, and the
ranking re-derives itself after every batch.

- [ ] **Step 2: Work the first batch of eight**

For each module in the batch, in order: close its mechanical blockers (the gates from Tasks 7–10),
re-run `node tooling/gates/run-ui-gate-model.mjs`, and confirm every gate except
`assertDesignReview` reads PASS or NOT-APPLICABLE.

- [ ] **Step 3: Queue the batch for design review**

```bash
npm run gallery:prepare && npm run gallery:react
```

Review each module's stories in the running gallery. Then, per module, with the reviewer present:

```bash
node tooling/gates/record-design-verdict.mjs --module <moduleId> --verdict approved --reviewer "Chris Wood" --notes "…"
```

The tool computes the reference revision itself from the module's scenario catalog **and** its
`style.css`, so the verdict expires the moment either changes. Do not pass a revision by hand.

- [ ] **Step 4: A `changes-requested` verdict is a result, not a failure**

Badge's first review returned `changes-requested` and that is what proved ruling 2. When one comes
back, fix the design, and the verdict expires itself against the new revision — observed end to end
on badge already. Re-review after the fix; do not edit the record.

- [ ] **Step 5: Re-rank and repeat**

```bash
node tooling/gates/run-ui-gate-model.mjs
node -e "const r=require('./docs/evidence/gate-model/ui-gate-model.json');console.log(r.modules.filter(m=>m.terminal).length+' of 76 terminal')"
```

Repeat steps 1–4 until the count reads 76. Commit after each batch:

```bash
git add -A && git commit -m "Matrix batch N: <module ids> reach terminal"
```

- [ ] **Step 6: Run the full gate once the count reaches 76**

```bash
npm run gate:phase4 && npm run receipt:phase4
```

Expected: `status: PASS`, 16 steps.

---

### Task 13: Define and enforce `implemented-gate-model-green`

Until this task, "terminal" is a field in a receipt nobody checks. This makes the claim
un-fakeable: a module may carry the terminal status only when the receipt says it earned it.

**Files:**
- Modify: `tooling/validate-repository.mjs` (near line 56, `allowedStatuses`; and the module loop at ~line 270)
- Modify: `catalog/modules.yaml` (the 76 `hlp.ui.*` status values)
- Modify: `tooling/gates/run-ui-gate-model.mjs` (nothing — the receipt already carries `terminal`)

**Interfaces:**
- Consumes: `docs/evidence/gate-model/ui-gate-model.json`
- Produces: `validate-repository.mjs` errors with
  `<moduleId>: status implemented-gate-model-green but the gate-model receipt reports <gateId>=<status>`

- [ ] **Step 1: Write the failing check — perturb a status before the validator can see it**

```bash
cd /c/Projects/Harborline/harborline-platform
node -e "
const fs=require('fs');const p='catalog/modules.yaml';
const s=fs.readFileSync(p,'utf8');
fs.writeFileSync(p+'.bak',s);
fs.writeFileSync(p,s.replace('\"hlp.ui.spinner\": {\n      \"interface\"','\"hlp.ui.spinner\": {\n      \"interface\"'));
"
```

Then hand-edit `hlp.ui.spinner`'s `status` to `"implemented-gate-model-green"` while its receipt row
is not terminal, and run:

Run: `node tooling/validate-repository.mjs --allow-stale-gate`
Expected: **PASS** — which is the defect. The validator does not look at module status at all today.

- [ ] **Step 2: Add the status vocabulary and the receipt check**

```javascript
// Module status was validated by NOTHING -- only projection status was (line 273). So every UI
// module has carried `extracted-candidate` or `gap-implemented` since it was written, and either
// could have been changed to anything at all without a check noticing. A status that cannot be
// wrong is not a status; it is a comment.
const allowedModuleStatuses = new Set([
  'extracted-candidate', 'gap-implemented', 'implemented',
  'implemented-package-consumer-green', 'draft-package-consumer-green', 'draft-native-green',
  'implemented-engine-substrate-package-green', 'implemented-gate-model-green',
])

const gateModelPath = resolve(root, 'docs/evidence/gate-model/ui-gate-model.json')
const gateModel = statSync(gateModelPath, {throwIfNoEntry: false})
  ? JSON.parse(readFileSync(gateModelPath, 'utf8'))
  : null
const gateModelByModule = Object.fromEntries((gateModel?.modules ?? []).map(entry => [entry.moduleId, entry]))
```

and in the per-module loop:

```javascript
  if (!allowedModuleStatuses.has(module.status)) errors.push(`${moduleId}: unknown module status ${module.status}`)
  if (module.status === 'implemented-gate-model-green') {
    const row = gateModelByModule[moduleId]
    if (!row) {
      errors.push(`${moduleId}: status implemented-gate-model-green but the gate-model receipt has no row for it`)
    } else if (!row.terminal) {
      const blocking = row.gates.filter(gate => gate.status !== 'PASS' && gate.status !== 'NOT-APPLICABLE')
      errors.push(`${moduleId}: status implemented-gate-model-green but the gate-model receipt reports ${blocking.map(gate => `${gate.id}=${gate.status}`).join(', ')}`)
    }
  }
```

- [ ] **Step 3: Run the validator to verify it now fails**

Run: `node tooling/validate-repository.mjs --allow-stale-gate`
Expected: FAIL with
`hlp.ui.spinner: status implemented-gate-model-green but the gate-model receipt reports …`

- [ ] **Step 4: Revert the perturbation and verify green**

```bash
mv catalog/modules.yaml.bak catalog/modules.yaml
node tooling/validate-repository.mjs --allow-stale-gate
```

Expected: PASS. That is the positive control ticket 098 acceptance 3 asks for, applied to the
validator rather than to a scanner.

- [ ] **Step 5: Promote every terminal module**

```bash
node -e "
const fs=require('fs');
const receipt=JSON.parse(fs.readFileSync('docs/evidence/gate-model/ui-gate-model.json','utf8'));
const terminal=new Set(receipt.modules.filter(m=>m.terminal).map(m=>m.moduleId));
let text=fs.readFileSync('catalog/modules.yaml','utf8'), changed=0;
for(const id of terminal){
  // Textual, per module: a JSON round-trip of this file rewrites its \\uXXXX escapes and churns
  // unrelated lines -- the same trap host-test-baseline.json carries.
  const re=new RegExp('(\"'+id.replace(/\\./g,'\\\\.')+'\":[\\\\s\\\\S]*?\"status\": \")[a-z-]+(\")');
  const next=text.replace(re,(m,a,b)=>{changed++;return a+'implemented-gate-model-green'+b});
  if(next!==text) text=next;
}
fs.writeFileSync('catalog/modules.yaml',text);
console.log(changed+' modules promoted to implemented-gate-model-green');
"
```

Expected: `76 modules promoted to implemented-gate-model-green`.

- [ ] **Step 6: Full gate, then the receipt**

```bash
npm run gate:phase4 && npm run receipt:phase4
```

Expected: `status: PASS`, 16 steps. `catalog-final` runs `validate-repository.mjs` after
`ui-gate-model` has written a fresh receipt, so the status claim is checked against a receipt
generated in the same run — not a stale one.

- [ ] **Step 7: Close ticket 098's remaining acceptances**

Append to `tickets/T-098-the-gate-model/ticket.md`:

```markdown
## Acceptances 1, 3, 5 and 7 — closed (2026-08-25)

| criterion | state |
|---|---|
| 1 — gate sets as named dimensions, `largeData`/`large-data` reconciled, all 76 with an explicit disposition | **MET** — `quality.yaml` gains a `gates` block; the 13 camelCase spellings collapsed to `large-data`; 17 non-presentational modules carry `not-applicable` with a rationale and a decider |
| 3 — `assertGateCanFail` has a canary per gate, each observed to fail | **MET** — `tooling/gates/verify-canaries.sh`, plus the receipt's own terminal-predicate canary |
| 5 — a per-module disposition a person decided | **MET** — `decidedBy` is a required field; `declaredDisposition()` throws without it |
| 7 — the static sweep runs against 76 modules in under two minutes and its output IS the triage | **MET** — `npm run gates:ui` writes `docs/evidence/gate-model/ui-gate-model.json`; `/tmp/matrix-ranking.txt` was derived from it, not hand-made |

Acceptance 9 (API seam `assertDeliverySemantics`) is phase 5 and remains open. It is Tier 3 and does
not gate any UI module's status.

The harness now lives in `harborline-platform/tooling/gates/` and runs as the `ui-gate-model`
phase-4 step. It measured this repository from outside it for the whole of phases 1 and 2, which is
why nothing could fail because of it.
```

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "A module status that has to be earned, and a validator that checks the receipt"
```

---

## Self-review

**Spec coverage.** Every acceptance in the four component tickets and in ticket 098's UI-facing
scope maps to a task:

| requirement | task |
|---|---|
| 090 acceptances 1–3 | Task 3 |
| 101 acceptances 1–3 | Tasks 4 (2), 5 (1, 3) |
| 102 acceptances 1–3 | Task 3 (already met; register only) |
| 106 acceptances 1–3 | already met before this plan; **4** Task 6, **5** Task 6 step 7 |
| 098 acceptance 1 | Tasks 11 (dispositions, `large-data`), 2 (`gates` schema) |
| 098 acceptance 2 | already MET; preserved by Task 2's single `gateRows()` |
| 098 acceptance 3 | Tasks 1, 2, 4, 5, 8, 13 — each ships its canary |
| 098 acceptance 4 | already MET |
| 098 acceptance 5 | Task 11 (`decidedBy` required, throws without) |
| 098 acceptance 6 | no gate description added here names a vendor library |
| 098 acceptance 7 | Task 2 (the receipt), Task 12 step 1 (the ranking derives from it) |
| 098 acceptance 8 | already MET |
| 098 acceptance 9 | **out of scope** — Tier 3, API seams, ticket 098 phase 5 |

**Known gaps, stated rather than hidden:**

- **Ticket 098 acceptance 9 is not in this plan.** It is the API seam inventory, Tier 3, and gates
  no UI module's status. It needs its own plan.
- **`hlp.ui.search-input`'s two determinism findings** are not addressed by Task 4 — that fix is
  about lane symmetry, and search-input's findings are a genuine debounce timer. It will surface as
  a non-terminal module in Task 12's ranking and be fixed there.
- **58 human design reviews are the critical path** and cannot be compressed by tooling. Task 12
  states this rather than costing it as engineering time.
- **Task 10 step 5 grows the gallery gate's wall clock.** The measured baseline is 38.2 minutes at
  437 tests on two cores; three viewports applied naively triples it. The step scopes the range to
  two surfaces and requires the new measured duration in the commit message, but if it lands above
  ~45 minutes it needs ticket 098 phase 3's sharding before Task 12 batches against it.

**Type consistency.** `gateRows(ctx) -> [{id, parent, status, note}]` is used identically in Tasks 2,
7, 8, 9, 10 and 11. `terminalFor(rows)` (Task 2) is the only definition of terminal and is what
Task 13's validator check reads via the receipt's `terminal` boolean. `declaredDisposition(quality,
gateId)` (Task 11) returns the same `[status, note]` tuple shape as every `verdict()`.

**Statuses are a closed set** — `PASS`, `PARTIAL`, `FAIL`, `VOID`, `UNBUILT`, `NOT-APPLICABLE` — and
`TERMINAL` in Task 2 admits exactly the last two of the first and last.
