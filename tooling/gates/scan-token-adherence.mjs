#!/usr/bin/env node
// Vendored from harborline-control/tools/scan-token-adherence.mjs on 2026-08-25.
//
// It measured this repository from outside it, so nothing here could ever fail because of it and no
// module status it produced could mean anything. Same reason eng/run-exact-clone.mjs was vendored
// into harborline-api. The control-repo copy is deleted, not forked: a tool in two places is a
// second register, and this programme has already paid for one of those.
// Ticket 098 phase 1 — the cheap half of assertTokenAdherence.
//
// Every colour, radius, and elevation value should resolve to a design token. This reads BOTH
// lanes' stylesheets and reports the ones that do not, WITHOUT a browser.
//
// CLASSIFICATION IS THE WHOLE JOB, and a raw count is worse than no count. The first measurement of
// this corpus reported 637 colour literals in the React lane. On inspection **582 of them are
// `var(--token, #fallback)` fallbacks** — the correct pattern, a token read with a default — and 10
// more are `mask-image` colours where the value is a mask channel and carries no design meaning.
// The real number is roughly 22 direct declarations plus about 23 module-local token definitions,
// which is a different conversation and a very different amount of work. Reporting 637 would have
// been the third headline number in one day to dissolve on inspection.
//
// Categories, in the order they are tested:
//   var-fallback              var(--x, #fff)         NOT a finding: the token is read
//   mask                      mask-image: ...#000    NOT a finding: a mask channel, not a colour
//   module-local-token        --hl-x-danger: #b42318 finding, low: a module defines its own palette
//                                                    from literals rather than global tokens
//   direct-literal            color: #fff            finding: a value with no token behind it
//
// It REPORTS; it never fails a build. Flipping to failing needs a closed classification, every
// existing finding dispositioned, and this canary walking the real pipeline — the third is done.
//
// Usage:
//   node scan-token-adherence.mjs [--platform <path>] [--json] [--module <id>]
//   node scan-token-adherence.mjs --canary
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

// `property` is stated where the pattern already names it. The colour pattern matches a bare value
// anywhere, so its property has to be recovered from the text before it; the shadow and radius
// patterns anchor on their own property name, and their match index points at the PROPERTY rather
// than at the value — so recovering it from context finds the previous declaration instead. That
// bug shipped in the first draft and the canary caught it: `border-radius: 9999px` came back as a
// direct-literal because the benign check never saw the property it was keyed to.
const VALUE_PATTERNS = [
  ['colour', /(?<![\w-])(#[0-9a-fA-F]{3,8}\b|rgba?\s*\([^)]*\)|hsla?\s*\([^)]*\)|oklch\s*\([^)]*\))/g, null],
  ['shadow', /box-shadow\s*:\s*(?!var\()(?!none)([^;}]+)/g, 'box-shadow'],
  ['radius', /border-radius\s*:\s*(?!var\()([^;}]+)/g, 'border-radius'],
]

// Radius values that carry no design decision: a square corner, a pill, and a full circle.
const BENIGN_RADIUS = /^\s*(0|0px|0%|9999px|50%|100%|inherit|initial|unset)\s*$/

/**
 * Decides what a matched value means in the declaration it sits in.
 * @param {string} css The whole stylesheet.
 * @param {number} at Index the match starts at.
 * @param {string} value The matched text.
 * @param {string|null} known Property named by the pattern itself, when it names one.
 * @returns {string} One of var-fallback, mask, module-local-token, direct-literal, benign.
 */
function classify(css, at, value, known) {
  const before = css.slice(Math.max(0, at - 160), at)
  // Inside an unclosed var( ... — i.e. this is the fallback argument.
  const openVar = before.lastIndexOf('var(')
  if (openVar >= 0 && before.slice(openVar).split(')').length === 1) return 'var-fallback'
  const declaration = /([-\w]+)\s*:\s*[^;{}]*$/.exec(before)
  const property = known ?? declaration?.[1] ?? ''
  if (/^(mask|mask-image|-webkit-mask|-webkit-mask-image)$/.test(property)) return 'mask'
  if (before.slice(-160).includes('url(')) return 'mask'
  if (property.startsWith('--')) return 'module-local-token'
  if (property === 'border-radius' && BENIGN_RADIUS.test(value)) return 'benign'
  if (known && /^\s*var\(/.test(value)) return 'var-fallback'
  return 'direct-literal'
}

/**
 * Scans one stylesheet.
 * @param {string} cssPath Path to a stylesheet.
 * @returns {object[]} One row per non-benign finding.
 */
function scanStylesheet(cssPath) {
  if (!existsSync(cssPath)) return []
  const css = readFileSync(cssPath, 'utf8')
  const rows = []
  for (const [kind, pattern, known] of VALUE_PATTERNS) {
    for (const match of css.matchAll(new RegExp(pattern))) {
      const value = (match[1] ?? match[0]).trim()
      const category = classify(css, match.index, value, known)
      if (category === 'var-fallback' || category === 'mask' || category === 'benign') continue
      rows.push({kind, category, value: value.slice(0, 48)})
    }
  }

  return rows
}

/**
 * Walks a platform tree, scanning both lanes for every module.
 * @param {string} root Platform repository root.
 * @param {string|null} moduleFilter Restrict to one module id, or null for all.
 * @returns {{moduleCount: number, sheetCount: number, modules: object[]}} The scan.
 */
function runScan(root, moduleFilter) {
  const specs = path.join(root, 'specs/modules/ui')
  if (!existsSync(specs)) throw new Error(`no UI spec root at ${specs}`)
  const modules = []
  let sheetCount = 0
  let moduleCount = 0
  for (const moduleId of readdirSync(specs).filter(name => name.startsWith('hlp.ui.')).sort()) {
    if (moduleFilter && moduleId !== moduleFilter) continue
    const reactSheet = path.join(specs, moduleId, 'style.css')
    if (!existsSync(reactSheet)) continue
    moduleCount += 1
    sheetCount += 1
    const react = scanStylesheet(reactSheet)
    const blazorRoot = path.join(root, 'projections/blazor/ui', moduleId, 'wwwroot')
    let blazor = []
    if (existsSync(blazorRoot)) {
      for (const name of readdirSync(blazorRoot).filter(entry => entry.endsWith('.css'))) {
        sheetCount += 1
        blazor = blazor.concat(scanStylesheet(path.join(blazorRoot, name)))
      }
    }

    if (react.length > 0 || blazor.length > 0) modules.push({moduleId, react, blazor})
  }

  return {moduleCount, sheetCount, modules}
}

if (canaryMode) {
  // assertGateCanFail, over the REAL walk against a planted tree — not the classifier alone.
  // The known-good sheet is the important half here: it uses var() fallbacks, a mask colour, a pill
  // radius and a square radius, all of which a naive scanner reports and which would drown the
  // signal in 582 non-findings.
  const root = mkdtempSync(path.join(tmpdir(), 'token-canary-'))
  const write = (relative, body) => {
    const target = path.join(root, relative)
    mkdirSync(path.dirname(target), {recursive: true})
    writeFileSync(target, body)
  }

  write('specs/modules/ui/hlp.ui.canary-bad/style.css',
    '.a{color:#ff0000;box-shadow:0 0 2px rgb(0 0 0/.2)}.b{--hl-canary-danger:#b42318}')
  write('projections/blazor/ui/hlp.ui.canary-bad/wwwroot/bad.css', '.c{color:rgb(1 2 3)}')
  write('specs/modules/ui/hlp.ui.canary-good/style.css',
    '.d{color:var(--hl-text,#ffffff);border-radius:9999px}.e{border-radius:0;mask-image:linear-gradient(#000,#000)}')

  const failures = []
  try {
    const result = runScan(root, null)
    const bad = result.modules.find(row => row.moduleId === 'hlp.ui.canary-bad')
    const good = result.modules.find(row => row.moduleId === 'hlp.ui.canary-good')
    if (result.moduleCount !== 2) failures.push(`module discovery found ${result.moduleCount}, expected 2`)
    if (result.sheetCount !== 3) failures.push(`sheet walk read ${result.sheetCount}, expected 3`)
    if (!bad) failures.push('known-bad module produced no finding')
    if (bad && !bad.react.some(row => row.category === 'direct-literal')) failures.push('known-bad: direct colour literal NOT detected')
    if (bad && !bad.react.some(row => row.category === 'module-local-token')) failures.push('known-bad: module-local token definition NOT detected')
    if (bad && bad.blazor.length === 0) failures.push('blazor lane stylesheet NOT scanned')
    if (good) failures.push(`known-good falsely flagged: ${JSON.stringify(good).slice(0, 160)}`)
  } catch (error) {
    failures.push(`the walk threw: ${error instanceof Error ? error.message : String(error)}`)
  } finally {
    rmSync(root, {recursive: true, force: true})
  }

  if (failures.length > 0) {
    const detail = failures.map(line => `  ${line}`).join('\n')
    process.stderr.write(`canary FAIL — the token-adherence scanner is not effective:\n${detail}\n`)
    process.exit(1)
  }

  process.stdout.write('canary OK — real walk over a planted tree: direct literals and module-local\n'
    + '  tokens flagged in both lanes; var() fallbacks, masks and benign radii stay clean\n')
  process.exit(0)
}

let scan
try {
  scan = runScan(platformRoot, onlyModule)
} catch (error) {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`)
  process.exit(2)
}

if (asJson) {
  process.stdout.write(`${JSON.stringify({generated: true, ...scan}, null, 2)}\n`)
  process.exit(0)
}

const tally = rows => rows.reduce((counts, row) => {
  const key = `${row.category}/${row.kind}`
  counts[key] = (counts[key] ?? 0) + 1
  return counts
}, {})
const total = category => scan.modules.reduce((sum, row) =>
  sum + [...row.react, ...row.blazor].filter(entry => entry.category === category).length, 0)

process.stdout.write(`${scan.moduleCount} modules, ${scan.sheetCount} stylesheets across both lanes\n`)
process.stdout.write(`  direct-literal      ${String(total('direct-literal')).padStart(4)}  a value with no token behind it\n`)
process.stdout.write(`  module-local-token  ${String(total('module-local-token')).padStart(4)}  a module defining its own palette from literals\n\n`)

for (const row of scan.modules.sort((a, b) =>
  (b.react.length + b.blazor.length) - (a.react.length + a.blazor.length))) {
  const r = tally(row.react)
  const b = tally(row.blazor)
  const fmt = counts => Object.entries(counts).map(([k, v]) => `${k}=${v}`).join(' ') || '-'
  const asymmetric = row.react.length !== row.blazor.length ? '  ASYMMETRIC' : ''
  process.stdout.write(`  ${row.moduleId.padEnd(28)} react[${fmt(r)}]\n`)
  process.stdout.write(`  ${''.padEnd(28)} blazor[${fmt(b)}]${asymmetric}\n`)
}

process.stdout.write('\nvar(--token, #fallback) is the CORRECT pattern and is not reported: the token is\n'
  + 'read, the literal is only the default. 582 of this corpus\'s 637 colour literals are that\n'
  + 'shape, which is why a raw count would have been off by a factor of thirty.\n')
