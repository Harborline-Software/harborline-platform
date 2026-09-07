#!/usr/bin/env node
// Vendored from harborline-control/tools/scan-catalog-story-drift.mjs on 2026-08-25.
//
// It measured this repository from outside it, so nothing here could ever fail because of it and no
// module status it produced could mean anything. Same reason eng/run-exact-clone.mjs was vendored
// into harborline-api. The control-repo copy is deleted, not forked: a tool in two places is a
// second register, and this programme has already paid for one of those.
// Ticket 102 option 2 — reconcile the scenario catalog against the stories it describes.
//
// specs/modules/ui/<id>/scenarios.json carries content.props that LOOK like the scenario's
// implementation. They are not: the gallery renders from gallery/projections/{react,blazor}, which
// hold their own literals, and nothing checks the two agree. Ticket 097's fix changed a duration in
// the catalog only, passed a 15/15 gate, a PR and a merge, and CI came back byte-identical.
//
// Scope is deliberately narrow: only the props ticket 098's determinism scanner treats as TIMING.
// Those are the load-bearing subset — read by a gate, and the ones whose drift is silent in both
// directions. Reconciling every prop would drown in the stories' legitimate conditionals
// (`maximumVisible: id === 'toaster.queue-lifecycle' ? 2 : 6`), which is option 1's problem.
//
// Reports; does not fail a build.
//   node scan-catalog-story-drift.mjs [--platform <path>] [--json]
//   node scan-catalog-story-drift.mjs --canary
import {readFileSync, readdirSync, existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import path from 'node:path'

import {componentStem} from './design-review.mjs'

// This tool now lives inside the repository it measures: the root is two levels up from here.
const defaultPlatformRoot = `${import.meta.dirname}/../..`

const argv = process.argv.slice(2)
const at = argv.indexOf('--platform')
const platformRoot = path.resolve(at >= 0 && argv[at + 1] ? argv[at + 1] : defaultPlatformRoot)

const TIMING = new Set(['duration', 'delay', 'delayDuration', 'debounce', 'debounceMs', 'throttle',
  'timeout', 'interval', 'pollInterval', 'autoClose', 'autoDismiss', 'autoHide', 'dismissAfter',
  'animationDuration', 'transitionDuration'])

// One spelling of "which component is this module": design-review.mjs derives the same stem to
// name the story files a design verdict binds (ticket 138), and two copies of that rule would drift.
const pascal = componentStem

function runScan(root) {
  const specs = path.join(root, 'specs/modules/ui')
  if (!existsSync(specs)) throw new Error(`no UI spec root at ${specs}`)
  const findings = []
  const rendered = []
  let checked = 0
  let presenceChecked = 0
  for (const moduleId of readdirSync(specs).filter(n => n.startsWith('hlp.ui.')).sort()) {
    const catalogPath = path.join(specs, moduleId, 'scenarios.json')
    if (!existsSync(catalogPath)) continue
    const stem = pascal(moduleId)
    const sources = []
    for (const [lane, dir, match] of [
      ['react', path.join(root, 'gallery/projections/react/src'), n => n.startsWith(`${stem}.`)],
      ['blazor', path.join(root, 'gallery/projections/blazor/Stories'), n => n.startsWith(stem)],
    ]) {
      if (!existsSync(dir)) continue
      for (const name of readdirSync(dir).filter(match)) {
        sources.push({lane, text: readFileSync(path.join(dir, name), 'utf8'), file: name})
      }
    }

    if (sources.length === 0) continue
    for (const scenario of JSON.parse(readFileSync(catalogPath, 'utf8')).scenarios ?? []) {
      // PRESENCE, alongside the timing reconciliation below. A catalog scenario is prose until a
      // story draws it -- that is this ticket's whole finding -- and the gallery runs one browser
      // test per scenario id it finds in the stories. So "the id appears in both lanes' stories" is
      // the difference between a scenario that was declared and one that is rendered and scanned,
      // which is what assertEmptyAndErrorStates and assertContentResilience need before either can
      // honestly return PASS instead of "declared; rendering UNBUILT".
      for (const lane of ['react', 'blazor']) {
        const laneSources = sources.filter(s => s.lane === lane)
        if (laneSources.length === 0) continue
        presenceChecked += 1
        if (laneSources.some(s => s.text.includes(scenario.id))) {
          rendered.push(`${moduleId}|${scenario.id}|${lane}`)
          continue
        }
        findings.push({moduleId, scenarioId: scenario.id, prop: '(scenario)', catalog: 'declared',
          lane, kind: 'absent-from-story', files: laneSources.map(s => s.file)})
      }

      for (const [prop, value] of Object.entries(scenario.content?.props ?? {})) {
        if (!TIMING.has(prop)) continue
        if (typeof value !== 'number' || value === 0) continue
        checked += 1
        // The story may write 600000 or 6e5; accept either spelling of the same number.
        const forms = [String(value), value.toExponential().replace('e+', 'e')]
        for (const lane of ['react', 'blazor']) {
          const laneSources = sources.filter(s => s.lane === lane)
          if (laneSources.length === 0) continue
          if (laneSources.some(s => forms.some(f => s.text.includes(f)))) continue
          findings.push({moduleId, scenarioId: scenario.id, prop, catalog: value, lane,
            files: laneSources.map(s => s.file)})
        }
      }
    }
  }

  return {checked, presenceChecked, rendered, findings}
}

if (argv.includes('--canary')) {
  const root = mkdtempSync(path.join(tmpdir(), 'drift-canary-'))
  const write = (rel, body) => {
    mkdirSync(path.dirname(path.join(root, rel)), {recursive: true})
    writeFileSync(path.join(root, rel), body)
  }
  const scen = id => JSON.stringify({scenarios: [{id, content: {props: {duration: 600000}}}]})
  // Each fixture's stories now carry their own scenario id, because presence is checked alongside
  // the timing prop and a story that never names its scenario is a separate defect from one whose
  // number drifted. Keeping them separable is what lets the two verdicts below mean different things.
  write('specs/modules/ui/hlp.ui.canary-bad/scenarios.json', scen('bad.themed'))
  write('gallery/projections/react/src/CanaryBad.stories.tsx', 'bad.themed duration={4000}')
  write('gallery/projections/blazor/Stories/CanaryBadScenario.razor', 'bad.themed DurationMilliseconds="4000"')
  write('specs/modules/ui/hlp.ui.canary-good/scenarios.json', scen('good.themed'))
  write('gallery/projections/react/src/CanaryGood.stories.tsx', 'good.themed duration={600000}')
  write('gallery/projections/blazor/Stories/CanaryGoodScenario.razor', 'good.themed DurationMilliseconds="6e5"')
  // Declared in the catalog, drawn by neither lane: the state fifty-three modules would be in if
  // scenarios were added without stories, and the one assertEmptyAndErrorStates must not read as
  // rendered.
  write('specs/modules/ui/hlp.ui.canary-absent/scenarios.json',
    JSON.stringify({scenarios: [{id: 'absent.empty', content: {props: {}}}]}))
  write('gallery/projections/react/src/CanaryAbsent.stories.tsx', 'some other scenario')
  write('gallery/projections/blazor/Stories/CanaryAbsentScenario.razor', 'some other scenario')

  const failures = []
  try {
    const r = runScan(root)
    const bad = r.findings.filter(f => f.moduleId === 'hlp.ui.canary-bad')
    const good = r.findings.filter(f => f.moduleId === 'hlp.ui.canary-good')
    const absent = r.findings.filter(f => f.moduleId === 'hlp.ui.canary-absent')
    if (r.checked !== 2) failures.push(`checked ${r.checked} props, expected 2`)
    if (bad.length !== 2) failures.push(`known-bad produced ${bad.length} findings, expected 2 (both lanes)`)
    if (bad.some(f => f.kind === 'absent-from-story')) failures.push('known-bad names its scenario; only its timing prop drifted')
    if (good.length !== 0) failures.push(`known-good flagged: ${JSON.stringify(good)}`)
    if (absent.length !== 2) failures.push(`a scenario drawn by neither lane must be flagged in both, got ${absent.length}`)
    if (!absent.every(f => f.kind === 'absent-from-story')) failures.push('an undrawn scenario must be flagged as absent-from-story, not as a timing drift')
    if (r.rendered.includes('hlp.ui.canary-absent|absent.empty|react')) failures.push('an undrawn scenario must NOT be counted as rendered')
    if (!r.rendered.includes('hlp.ui.canary-good|good.themed|react')) failures.push('a drawn scenario must be counted as rendered')
  } catch (e) { failures.push(`walk threw: ${e.message}`) } finally { rmSync(root, {recursive: true, force: true}) }
  if (failures.length) {
    process.stderr.write(`canary FAIL:\n${failures.map(l => `  ${l}`).join('\n')}\n`)
    process.exit(1)
  }
  process.stdout.write('canary OK — real walk: drifted module flagged in both lanes, matching module\n'
    + '  clean including the 6e5 spelling of 600000, and a scenario drawn by neither lane flagged\n'
    + '  as absent rather than counted as rendered\n')
  process.exit(0)
}

const {checked, presenceChecked, rendered, findings} = runScan(platformRoot)
if (argv.includes('--json')) {
  process.stdout.write(`${JSON.stringify({generated: true, checked, presenceChecked, rendered, findings}, null, 2)}\n`)
  process.exit(0)
}
process.stdout.write(`${checked} timing props and ${presenceChecked} scenario presences reconciled`
  + ` against their stories; ${findings.length} drifted\n\n`)
for (const f of findings) {
  process.stdout.write(`  ${f.scenarioId.padEnd(30)} ${f.prop}=${f.catalog} absent from ${f.lane}`
    + ` (${f.files.join(', ')})\n`)
}
