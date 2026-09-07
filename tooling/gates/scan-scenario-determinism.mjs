#!/usr/bin/env node
// Vendored from harborline-control/tools/scan-scenario-determinism.mjs on 2026-08-25.
//
// It measured this repository from outside it, so nothing here could ever fail because of it and no
// module status it produced could mean anything. Same reason eng/run-exact-clone.mjs was vendored
// into harborline-api. The control-repo copy is deleted, not forked: a tool in two places is a
// second register, and this programme has already paid for one of those.
// Ticket 098 phase 1 — the cheap half of assertDeterminism.
//
// A parity scenario must not depend on how fast the machine is. This reads the UI Spec authority
// and both projections' stylesheets and reports what moves with wall-clock, WITHOUT a browser.
//
// It exists because ticket 097 was diagnosed the expensive way. `toaster.theme-light` declares
// `duration: 4000` and creates six toasts, two persistent and four expiring. On an 8-core host both
// lanes are captured before anything expires and the gate is green; on a 2-core runner React reaches
// "toasts shown" sooner than the server-circuit Blazor lane, so React screenshots 2 toasts and
// Blazor screenshots 4. Two CI campaigns, nine days, and a rationale that turned out to be false —
// and every input needed to predict it was in scenarios.json, readable in milliseconds.
//
// FAIL-CLOSED on vocabulary. The first version carried an allow-list of 13 timing prop names and
// silently ignored every other numeric prop. Within an hour it was shown to leak: `debounceMs` is
// not `debounce`, so `search-input.debounce` — a scenario named for its timer — went unreported.
// Every numeric prop is now classified as `timing` or `benign`, and an UNCLASSIFIED numeric prop is
// itself a finding. There are 28 distinct numeric prop names in the whole corpus, so the closed
// vocabulary costs one afternoon and never leaks again. This matches the rule the selection design
// already sets: no reliable mapping means expand, never silently skip.
//
// It REPORTS; it never fails a build. See "when this becomes a gate" below.
//
// Usage:
//   node scan-scenario-determinism.mjs [--platform <path>] [--json] [--module <id>]
//   node scan-scenario-determinism.mjs --canary     # assertGateCanFail: prove the scanner bites
import {readFileSync, readdirSync, existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import path from 'node:path'

// This tool now lives inside the repository it measures: the root is two levels up from here.
const defaultPlatformRoot = `${import.meta.dirname}/../..`

const argv = process.argv.slice(2)
const flag = (name, fallback) => {
  const index = argv.indexOf(name)
  return index >= 0 && argv[index + 1] ? argv[index + 1] : fallback
}
const asJson = argv.includes('--json')
const canaryMode = argv.includes('--canary')
const onlyModule = flag('--module', null)
const platformRoot = path.resolve(flag('--platform', defaultPlatformRoot))

// Closed vocabulary. Every numeric prop name in the corpus is here, classified. Adding a scenario
// with a new numeric prop reports `unclassified` until someone rules on it — which is the point.
const NUMERIC_PROPS = new Map(Object.entries({
  // governs WHEN something changes on screen
  duration: 'timing',
  delay: 'timing',
  delayDuration: 'timing',
  debounce: 'timing',
  debounceMs: 'timing',
  throttle: 'timing',
  timeout: 'timing',
  interval: 'timing',
  pollInterval: 'timing',
  autoClose: 'timing',
  autoDismiss: 'timing',
  autoHide: 'timing',
  dismissAfter: 'timing',
  animationDuration: 'timing',
  transitionDuration: 'timing',
  // governs HOW MUCH is on screen — stable across runs
  maxVisible: 'benign',
  maximumVisible: 'benign',
  rowCount: 'benign',
  rowHeight: 'benign',
  itemCount: 'benign',
  navigationItemCount: 'benign',
  messageCount: 'benign',
  columnCount: 'benign',
  taskCount: 'benign',
  dependencyCount: 'benign',
  updateCount: 'benign',
  rows: 'benign',
  maxLength: 'benign',
  // geometry — stable across runs
  width: 'benign',
  height: 'benign',
  top: 'benign',
  left: 'benign',
  min: 'benign',
  max: 'benign',
  step: 'benign',
  fadeSize: 'benign',
  endPanelWidth: 'benign',
  maxInlineSize: 'benign',
  zoomFactor: 'benign',
  stageMaxWidth: 'benign',
}))

// Constructs in a scenario's raw body that resolve on their own schedule rather than on render.
// Each row pairs equivalent spellings so lane parity measures the fixture rather than the scanner's
// language coverage. A null records that the language genuinely has no corresponding construct.
const ASYNC_PATTERNS = [
  ['deferred-callback', /\bsetTimeout\s*\(/, /\b(?:Timer|DelayAsync)\s*\(/],
  ['interval', /\bsetInterval\s*\(/, /\bPeriodicTimer\s*\(/],
  ['animation-frame', /\brequestAnimationFrame\s*\(/, null],
  ['wall-clock-read', /\b(?:Date\.now\s*\(|new\s+Date\b)/, /\b(?:DateTime|DateTimeOffset)\.(?:Now|UtcNow)\b/],
  ['random', /\bMath\.random\s*\(/, /\b(?:new\s+)?Random(?:\.Shared)?\b/],
  ['sleep', /\bsetTimeout\s*\((?:[^()]|\([^()]*\))*?,\s*\d+(?:\.\d+)?\s*\)/, /\bTask\.Delay\s*\(/],
  // Task.CompletedTask is deliberately ABSENT. It is the return of a no-op async-signature event
  // handler -- `onSelect: () => Task.CompletedTask` -- whose React twin is `onSelect: () => {}`,
  // which this list does not match either. Including it flagged five scenarios across spotlight and
  // data-export-button as lane-asymmetric when both lanes were doing the identical nothing, which is
  // the same defect this row was rewritten to fix, pointing the other way. The twin of
  // Promise.resolve(x) is Task.FromResult(x): a value-producing operation, not a constant.
  ['settled-async', /\bPromise\s*\.\s*(?:resolve|reject)\b/, /\bTask\.(?:FromResult|FromException)\s*(?:<[^>]+>)?\s*\(/],
  ['pending-async', /\b(?:new\s+Promise\b|Promise\s*\.(?!\s*(?:resolve|reject)\b))/, /\bTask\.(?:Run|WhenAll|WhenAny)\s*(?:<[^>]+>)?\s*\(/],
  ['await', /\bawait\s+/, /\bawait\s+/],
  ['continuation', /\.then\s*\(/, /\.ContinueWith\s*\(/],
]

const dedupe = rows => [...new Map(rows.map(row => [JSON.stringify(row), row])).values()]

/**
 * Finds the wall-clock dependencies declared by one scenario.
 *
 * Reported by AXIS, never by severity. Severity here would be a guess dressed as a measurement:
 * whether a declared timer actually produces a flake depends on whether the capture settles before
 * it fires, which is a runtime question this scanner deliberately does not ask. The axis IS
 * observable, and each axis has a different remedy — a prop timer is fixed in the fixture, an async
 * construct by awaiting a settled state, an animation by the reduced-motion guard.
 * @param {object} scenario A scenario entry from scenarios.json.
 * @returns {{timing: object[], unclassified: object[], async: object[]}} Findings with evidence.
 */
function scanScenario(scenario) {
  const content = scenario.content ?? {}
  const timing = []
  const unclassified = []
  for (const [key, value] of Object.entries(content.props ?? {})) {
    if (typeof value !== 'number' || typeof value === 'boolean' || !Number.isFinite(value)) continue
    const kind = NUMERIC_PROPS.get(key)
    if (kind === undefined) {
      unclassified.push({prop: key, value})
      continue
    }

    // A zero timer is not a timer: `delayDuration: 0` means the tooltip opens on the same frame.
    if (kind === 'timing' && value !== 0) timing.push({prop: key, ms: value})
  }

  const async_ = []
  for (const entry of [...(content.body ?? []), ...(content.footer ?? [])]) {
    for (const lane of ['react', 'blazor']) {
      const text = typeof entry?.[lane] === 'string' ? entry[lane] : null
      if (!text) continue
      for (const [name, reactPattern, blazorPattern] of ASYNC_PATTERNS) {
        const pattern = lane === 'react' ? reactPattern : blazorPattern
        if (!pattern) continue
        if (pattern.test(text)) async_.push({lane, construct: name})
      }
    }
  }

  return {timing, unclassified, async: dedupe(async_)}
}

/**
 * Counts animation in one stylesheet.
 * @param {string} cssPath Path to a stylesheet; missing files return null.
 * @returns {{keyframes: number, animation: number, guarded: boolean}|null} Null when it does not animate.
 */
function scanStylesheet(cssPath) {
  if (!existsSync(cssPath)) return null
  const css = readFileSync(cssPath, 'utf8')
  const count = pattern => (css.match(pattern) ?? []).length
  const keyframes = count(/@keyframes\s/g)
  const animation = count(/^[\t ]*animation(-name)?\s*:/gm)
  if (keyframes === 0 && animation === 0) return null
  return {keyframes, animation, guarded: /prefers-reduced-motion/.test(css)}
}

/**
 * Scans BOTH lanes' stylesheets for a module.
 *
 * The spec's style.css is copied into the React projection by sync-ui-spec-authority.mjs, so
 * scanning the spec covers React. Blazor authors its own under wwwroot/ — 58 of them — and reading
 * only the spec meant this gate was blind in exactly the lane the divergence it hunts lives in.
 * @param {string} root Platform repository root.
 * @param {string} moduleId The hlp.ui.* module id.
 * @returns {{react: object|null, blazor: object|null}|null} Null when neither lane animates.
 */
function scanBothLanes(root, moduleId) {
  const react = scanStylesheet(path.join(root, 'specs/modules/ui', moduleId, 'style.css'))
  const blazorRoot = path.join(root, 'projections/blazor/ui', moduleId, 'wwwroot')
  let blazor = null
  if (existsSync(blazorRoot)) {
    for (const name of readdirSync(blazorRoot).filter(entry => entry.endsWith('.css'))) {
      const found = scanStylesheet(path.join(blazorRoot, name))
      if (!found) continue
      blazor = blazor
        ? {keyframes: blazor.keyframes + found.keyframes,
           animation: blazor.animation + found.animation,
           guarded: blazor.guarded && found.guarded}
        : found
    }
  }

  return react || blazor ? {react, blazor} : null
}

/**
 * Plants a two-module platform tree on disk: one known-bad, one known-good.
 * @returns {string} The temporary root, for the caller to remove.
 */
function plantCanaryTree() {
  const root = mkdtempSync(path.join(tmpdir(), 'determinism-canary-'))
  const write = (relative, body) => {
    const target = path.join(root, relative)
    mkdirSync(path.dirname(target), {recursive: true})
    writeFileSync(target, body)
  }

  write('specs/modules/ui/hlp.ui.canary-bad/scenarios.json', JSON.stringify({scenarios: [{
    id: 'canary-bad.timed',
    content: {props: {duration: 4000, settleAfterMs: 250}, body: [{control: 'raw', react: "void Promise.resolve()"}]},
  }]}))
  // Deliberately UNGUARDED: no prefers-reduced-motion block, so the canary also proves the guard
  // detection distinguishes guarded from unguarded rather than reporting every sheet the same.
  write('specs/modules/ui/hlp.ui.canary-bad/style.css', '@keyframes spin { to { rotate: 360deg } }')
  write('projections/blazor/ui/hlp.ui.canary-bad/wwwroot/bad.css', '.x {\n  animation: spin 1s linear infinite;\n}')
  write('specs/modules/ui/hlp.ui.canary-good/scenarios.json', JSON.stringify({scenarios: [{
    id: 'canary-good.static',
    content: {props: {maximumVisible: 6, delayDuration: 0}, body: [{control: 'button', text: 'Show'}]},
  }]}))
  return root
}

if (canaryMode) {
  // assertGateCanFail. A scanner nobody has watched fire is not yet a scanner, and the first draft
  // of the analyzer canaries in harborline-api failed on CS8955 while the analyzer never ran at all.
  //
  // This drives the REAL walk over a planted tree, so it covers module discovery, file reading,
  // JSON parsing, BOTH stylesheet lanes, and aggregation — not just the detector. The previous
  // version called scanScenario on synthetic objects and would have reported OK while the walk
  // found nothing at all.
  const root = plantCanaryTree()
  const failures = []
  try {
    // This twin pair names one settled construct in two languages. The earlier flat list named only
    // the JavaScript half, so a correct fixture reported an asymmetry that existed inside the gate.
    const twins = {
      id: 'canary.twins',
      content: {props: {}, body: [{
        control: 'raw',
        react: "void service.trackAsync(Promise.resolve('x'), {})",
        blazor: '_ = Toasts.TrackAsync(Task.FromResult("x"), "l", _ => "s", _ => "e");',
      }]},
    }
    const twinResult = scanScenario(twins)
    const settledAsync = twinResult.async.filter(row => row.construct === 'settled-async')
    const twinLanes = new Set(settledAsync.map(row => row.lane))
    if (settledAsync.length === 0) failures.push('twins: a settled-async construct must be detected')
    if (twinLanes.size !== 2) failures.push(`twins: both lanes must report the construct, got ${[...twinLanes].join(',') || 'none'}`)

    // The other direction, and the one that cost five false FAILs: a no-op event handler is not an
    // async construct in EITHER language, so neither lane may report one. `() => Task.CompletedTask`
    // is how C# spells `() => {}` when the delegate returns a Task; matching the C# spelling while
    // leaving the JS spelling unmatched recreates the lane asymmetry this row exists to remove.
    const noop = {
      id: 'canary.noop-handler',
      content: {props: {}, body: [{control: 'raw',
        react: "items: [{ id: 'a', onSelect: () => {} }]",
        blazor: 'Items = [new(Id: "a", OnSelect: () => Task.CompletedTask)];'}]},
    }
    const noopResult = scanScenario(noop)
    if (noopResult.async.length !== 0) {
      failures.push(`noop-handler: a no-op handler is not an async construct in either lane, got ${noopResult.async.map(row => `${row.lane}:${row.construct}`).join(' ')}`)
    }

    const result = runScan(root, null)
    const bad = result.findings.find(row => row.moduleId === 'hlp.ui.canary-bad')
    const good = result.findings.find(row => row.moduleId === 'hlp.ui.canary-good')
    const motion = result.motion.find(row => row.moduleId === 'hlp.ui.canary-bad')

    if (result.moduleCount !== 2) failures.push(`module discovery found ${result.moduleCount}, expected 2`)
    if (result.scenarioCount !== 2) failures.push(`scenario walk found ${result.scenarioCount}, expected 2`)
    if (!bad) failures.push('known-bad module produced no finding')
    if (bad && bad.timing.length === 0) failures.push('known-bad: finite duration NOT detected')
    if (bad && bad.async.length === 0) failures.push('known-bad: Promise NOT detected')
    if (bad && bad.unclassified.length === 0) failures.push('unknown numeric prop silently ignored — the vocabulary is not closed')
    if (good) failures.push('known-good module was falsely flagged')
    if (!motion) failures.push('stylesheet scan found no animation in the known-bad module')
    if (motion && !motion.react) failures.push('react lane stylesheet NOT scanned')
    if (motion && !motion.blazor) failures.push('blazor lane stylesheet NOT scanned — the lane gap has reopened')
    if (motion && motion.react && motion.react.guarded) failures.push('unguarded react stylesheet reported as guarded')
  } catch (error) {
    failures.push(`the walk threw: ${error instanceof Error ? error.message : String(error)}`)
  } finally {
    rmSync(root, {recursive: true, force: true})
  }

  if (failures.length > 0) {
    const detail = failures.map(line => `  ${line}`).join('\n')
    process.stderr.write(`canary FAIL — the determinism scanner is not effective:\n${detail}\n`)
    process.exit(1)
  }

  process.stdout.write('canary OK — real walk over a planted tree: 2 modules discovered, known-bad\n'
    + '  flagged on timing + async + unclassified, both stylesheet lanes read, known-good clean\n')
  process.exit(0)
}

/**
 * Walks a platform tree and returns everything the scan found.
 *
 * Extracted so the canary can drive the REAL walk over a planted fixture rather than calling the
 * detector on synthetic objects. The earlier canary tested `scanScenario` alone: if module
 * discovery silently matched zero directories, the scan would print "0 scenarios across 0 modules",
 * exit 0, and the canary would still report OK. A check that cannot detect its own pipeline
 * breaking is the exact failure this ticket exists to catch.
 * @param {string} root Platform repository root.
 * @param {string|null} moduleFilter Restrict to one module id, or null for all.
 * @returns {{moduleCount: number, scenarioCount: number, findings: object[], motion: object[]}} The scan.
 */
function runScan(root, moduleFilter) {
  const specs = path.join(root, 'specs/modules/ui')
  if (!existsSync(specs)) throw new Error(`no UI spec root at ${specs}`)
  const found = []
  const animated = []
  let scenarios = 0
  let modules = 0
  for (const moduleId of readdirSync(specs).filter(name => name.startsWith('hlp.ui.')).sort()) {
    if (moduleFilter && moduleId !== moduleFilter) continue
    const scenariosPath = path.join(specs, moduleId, 'scenarios.json')
    if (!existsSync(scenariosPath)) continue
    modules += 1
    const lanes = scanBothLanes(root, moduleId)
    if (lanes) animated.push({moduleId, ...lanes})
    const catalog = JSON.parse(readFileSync(scenariosPath, 'utf8'))
    for (const scenario of catalog.scenarios ?? []) {
      scenarios += 1
      const result = scanScenario(scenario)
      if (result.timing.length === 0 && result.async.length === 0 && result.unclassified.length === 0) continue
      found.push({moduleId, scenarioId: scenario.id, ...result})
    }
  }

  return {moduleCount: modules, scenarioCount: scenarios, findings: found, motion: animated}
}

let scan
try {
  scan = runScan(platformRoot, onlyModule)
} catch (error) {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`)
  process.exit(2)
}

const {moduleCount, scenarioCount, findings, motion} = scan

if (asJson) {
  process.stdout.write(`${JSON.stringify({generated: true, moduleCount, scenarioCount, findings, motion}, null, 2)}\n`)
  process.exit(0)
}

process.stdout.write(`${scenarioCount} scenarios across ${moduleCount} modules; `
  + `${findings.length} declare a wall-clock dependency\n\n`)
for (const row of findings) {
  const parts = [
    row.timing.map(t => `${t.prop}=${t.ms}ms`).join(' '),
    row.async.map(a => `${a.lane}:${a.construct}`).join(' '),
    row.unclassified.map(u => `UNCLASSIFIED ${u.prop}=${u.value}`).join(' '),
  ].filter(Boolean)
  process.stdout.write(`  ${row.scenarioId.padEnd(34)} ${parts.join('  ')}\n`)
}

const byModule = new Map()
for (const row of findings) byModule.set(row.moduleId, (byModule.get(row.moduleId) ?? 0) + 1)
process.stdout.write(`\n${byModule.size} of ${moduleCount} modules affected:\n`)
for (const [moduleId, count] of [...byModule].sort((a, b) => b[1] - a[1])) {
  process.stdout.write(`  ${moduleId.padEnd(30)} ${count}\n`)
}

// Lane-only animation is called out separately: it is a divergence in itself, and it is the case
// reading only the spec authority could never have seen.
const laneOnly = motion.filter(row => Boolean(row.react) !== Boolean(row.blazor))
const unguarded = motion.filter(row =>
  (row.react && !row.react.guarded) || (row.blazor && !row.blazor.guarded))
process.stdout.write(`\n${motion.length} modules animate; ${unguarded.length} unguarded, `
  + `${laneOnly.length} animate in ONE LANE ONLY\n`)
for (const row of motion) {
  const cell = lane => lane ? `k=${lane.keyframes} a=${lane.animation}${lane.guarded ? '' : ' UNGUARDED'}` : '-'
  const mark = Boolean(row.react) !== Boolean(row.blazor) ? '  LANE-ONLY' : ''
  process.stdout.write(`  ${row.moduleId.padEnd(28)} react[${cell(row.react)}]  blazor[${cell(row.blazor)}]${mark}\n`)
}

process.stdout.write('\nReported by axis, never by severity: whether a declared timer actually flakes\n'
  + 'depends on whether the capture settles before it fires, which is a runtime question this\n'
  + 'scanner does not ask. Each axis has its own remedy.\n')
