#!/usr/bin/env node
// Vendored from harborline-control/tools/scan-declaration-gaps.mjs on 2026-08-25.
//
// It measured this repository from outside it, so nothing here could ever fail because of it and no
// module status it produced could mean anything. Same reason eng/run-exact-clone.mjs was vendored
// into harborline-api. The control-repo copy is deleted, not forked: a tool in two places is a
// second register, and this programme has already paid for one of those.
// Ticket 098 phase 1 — the cheap half of assertStateCompleteness, assertEmptyAndErrorStates,
// assertContentResilience, and assertBrowserCoverage. One walk, because all four reduce to the same
// question: is a scenario declared for this concern?
//
// State DERIVATION (ruling 8) is not implemented: interface.yaml's `cases` are bare `{id}` with no
// prop declarations, so there is nothing to derive a required state set from. Presence is what the
// data supports today. assertDesignReview is not here either — no record schema exists yet, and
// checking for an undefined thing reports 76 of 76 without telling anyone anything.
import {readFileSync, readdirSync, existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import path from 'node:path'

// This tool now lives inside the repository it measures: the root is two levels up from here.
const defaultPlatformRoot = `${import.meta.dirname}/../..`

const argv = process.argv.slice(2)
const at = argv.indexOf('--platform')
const platformRoot = path.resolve(at >= 0 && argv[at + 1] ? argv[at + 1] : defaultPlatformRoot)

const CONCERNS = {
  states: /state/i,
  empty: /empty|no-results|zero/i,
  error: /error|invalid|failure/i,
  content: /long|overflow|truncat|dense|large-data/i,
}

function runScan(root) {
  const specs = path.join(root, 'specs/modules/ui')
  if (!existsSync(specs)) throw new Error(`no UI spec root at ${specs}`)
  const modules = []
  for (const moduleId of readdirSync(specs).filter(n => n.startsWith('hlp.ui.')).sort()) {
    const file = path.join(specs, moduleId, 'scenarios.json')
    if (!existsSync(file)) continue
    const ids = (JSON.parse(readFileSync(file, 'utf8')).scenarios ?? []).map(s => s.id)
    const missing = Object.entries(CONCERNS)
      .filter(([, rx]) => !ids.some(id => rx.test(id)))
      .map(([name]) => name)
    modules.push({moduleId, missing})
  }

  // assertBrowserCoverage: one config read. No `projects:` block means one browser, whatever the
  // exemption record says.
  const config = path.join(root, 'gallery/tests/playwright.config.ts')
  const browsers = existsSync(config) && /^\s*projects\s*:/m.test(readFileSync(config, 'utf8'))
    ? 'declared' : 'chromium-only'
  return {modules, browsers}
}

if (argv.includes('--canary')) {
  const root = mkdtempSync(path.join(tmpdir(), 'decl-canary-'))
  const write = (rel, body) => {
    mkdirSync(path.dirname(path.join(root, rel)), {recursive: true})
    writeFileSync(path.join(root, rel), body)
  }
  write('specs/modules/ui/hlp.ui.canary-bad/scenarios.json', JSON.stringify({scenarios: [{id: 'bad.defaults'}]}))
  write('specs/modules/ui/hlp.ui.canary-good/scenarios.json', JSON.stringify({scenarios: [
    {id: 'good.states'}, {id: 'good.empty'}, {id: 'good.error'}, {id: 'good.long-labels'}]}))
  const failures = []
  try {
    const r = runScan(root)
    const bad = r.modules.find(m => m.moduleId === 'hlp.ui.canary-bad')
    const good = r.modules.find(m => m.moduleId === 'hlp.ui.canary-good')
    if (r.modules.length !== 2) failures.push(`discovery found ${r.modules.length}, expected 2`)
    if (bad?.missing.length !== 4) failures.push(`known-bad missing ${bad?.missing.length}, expected 4`)
    if (good?.missing.length !== 0) failures.push(`known-good flagged: ${good?.missing}`)
    if (r.browsers !== 'chromium-only') failures.push('absent playwright config not reported as chromium-only')
  } catch (e) { failures.push(`walk threw: ${e.message}`) } finally { rmSync(root, {recursive: true, force: true}) }
  if (failures.length) {
    process.stderr.write(`canary FAIL:\n${failures.map(l => `  ${l}`).join('\n')}\n`)
    process.exit(1)
  }
  process.stdout.write('canary OK — real walk, known-bad missing all four, known-good clean\n')
  process.exit(0)
}

const {modules, browsers} = runScan(platformRoot)
if (argv.includes('--json')) {
  process.stdout.write(`${JSON.stringify({generated: true, modules, browsers}, null, 2)}\n`)
  process.exit(0)
}

const counts = Object.fromEntries(Object.keys(CONCERNS).map(k =>
  [k, modules.filter(m => m.missing.includes(k)).length]))
process.stdout.write(`${modules.length} modules with scenarios\n`)
for (const [name, n] of Object.entries(counts)) {
  process.stdout.write(`  no ${name.padEnd(8)} scenario  ${String(n).padStart(3)}\n`)
}
process.stdout.write(`  browser coverage        ${browsers}\n\n`)
const worst = modules.filter(m => m.missing.length === 4).map(m => m.moduleId)
process.stdout.write(`${worst.length} modules declare none of the four: ${worst.slice(0, 12).join(', ')}`
  + `${worst.length > 12 ? ` … +${worst.length - 12}` : ''}\n`)
