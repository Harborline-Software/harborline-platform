#!/usr/bin/env node
// Vendored from harborline-control/tools/scan-token-resolution.mjs on 2026-08-25.
//
// It measured this repository from outside it, so nothing here could ever fail because of it and no
// module status it produced could mean anything. Same reason eng/run-exact-clone.mjs was vendored
// into harborline-api. The control-repo copy is deleted, not forked: a tool in two places is a
// second register, and this programme has already paid for one of those.
// Ticket 106 acceptance 1. `assertTokenAdherence` classifies `var(--anything)` as adherent without
// ever asking whether the token resolves. hlp.ui.badge shipped through every green gate with nine
// declarations that silently did not apply, because an undefined custom property with no fallback is
// invalid at computed-value time -- no background, no radius, inherited colour.
//
// The first version of this measurement was WRONG and over-reported by 3x (it claimed 459 references
// across 52 modules). It compared references against gallery/styles/canvas.css alone and therefore
// counted two things that are not defects. This scanner exists so the number cannot be got wrong
// again by hand: it names all four resolution paths explicitly.
//
//   canvas       defined in gallery/styles/canvas.css                       -> fine
//   module-local defined in the module's OWN style.css                      -> fine
//   runtime      set inline by the component (style={{'--hl-x': ...}})      -> fine
//   fallback     var(--token, #literal), no definition anywhere             -> WARN
//   broken       no definition, no fallback                                 -> FAIL
//
// Usage: node scan-token-resolution.mjs <platform-root> [--json|--canary]

import {existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync} from 'node:fs'
import {dirname, join, resolve} from 'node:path'
import {tmpdir} from 'node:os'

// This tool now lives inside the repository it measures: the root is two levels up from here.
const defaultPlatformRoot = `${import.meta.dirname}/../..`

const VAR_REFERENCE = /var\(\s*(--hl-[a-z0-9-]+)([^)]*)\)/g
const DEFINITION = /(--hl-[a-z0-9-]+)\s*:/g
// A component setting a custom property writes it as an object key or a Razor style string.
const RUNTIME_SET = /['"`](--hl-[a-z0-9-]+)['"`]\s*:|--hl-[a-z0-9-]+\s*:\s*\$?\{|style="[^"]*(--hl-[a-z0-9-]+)\s*:/g

function collect(source, pattern) {
  const found = new Set()
  for (const match of source.matchAll(pattern)) for (const group of match.slice(1)) if (group) found.add(group)
  return found
}

function readIfPresent(path) {
  return existsSync(path) ? readFileSync(path, 'utf8') : ''
}

function projectionSources(root, moduleId) {
  let combined = ''
  for (const relative of [`projections/react/ui/${moduleId}/src`, `projections/blazor/ui/${moduleId}`]) {
    const dir = resolve(root, relative)
    if (!existsSync(dir)) continue
    for (const entry of readdirSync(dir, {withFileTypes: true})) {
      if (entry.isFile() && /\.(tsx|ts|razor|cs)$/.test(entry.name)) combined += readIfPresent(join(dir, entry.name))
    }
  }
  return combined
}

export function runScan(root) {
  const canvas = collect(readIfPresent(resolve(root, 'gallery/styles/canvas.css')), DEFINITION)
  const specsDir = resolve(root, 'specs/modules/ui')
  if (!existsSync(specsDir)) throw new Error(`no UI specs under ${specsDir}`)

  const findings = []
  // Same hlp.ui. prefix every sibling scanner discovers on. Two things were wrong without it:
  // specs/modules/ui/baselines/ is not a module and was being read as one, and verify-canaries.sh
  // perturbs exactly this prefix -- so with no prefix here the perturbation was a no-op and this
  // scanner's canary passed while discovery was broken. A canary that cannot be perturbed is the
  // DEAD gate assertGateCanFail exists to name.
  for (const moduleId of readdirSync(specsDir).filter(id => id.startsWith('hlp.ui.')).sort()) {
    const stylePath = resolve(specsDir, moduleId, 'style.css')
    if (!existsSync(stylePath)) continue
    const style = readFileSync(stylePath, 'utf8')
    const selfDefined = collect(style, DEFINITION)
    const runtimeSet = collect(projectionSources(root, moduleId), RUNTIME_SET)

    const seen = new Map()
    for (const match of style.matchAll(VAR_REFERENCE)) {
      const token = match[1]
      const hasFallback = Boolean(match[2].trim())
      // A token referenced with a fallback somewhere and without one elsewhere is still only as safe
      // as its worst site, so the strictest observation for a token wins.
      const prior = seen.get(token)
      if (prior === undefined || (prior === true && !hasFallback)) seen.set(token, hasFallback)
    }

    for (const [token, hasFallback] of seen) {
      if (canvas.has(token)) continue
      if (selfDefined.has(token)) continue
      if (runtimeSet.has(token)) continue
      findings.push({moduleId, token, severity: hasFallback ? 'warn' : 'fail'})
    }
  }

  const fails = findings.filter(f => f.severity === 'fail')
  const warns = findings.filter(f => f.severity === 'warn')
  return {
    canvasTokens: canvas.size,
    findings,
    counts: {fail: fails.length, warn: warns.length},
    brokenModules: [...new Set(fails.map(f => f.moduleId))].sort(),
  }
}

// ---- canary -----------------------------------------------------------------------------------
// Plants a tree where every resolution path is exercised, and requires the scanner to reach a
// different verdict on each. Without this, a scanner that returned [] would look like a clean repo.
function canary() {
  const root = resolve(tmpdir(), `hlp-token-resolution-canary-${process.pid}`)
  const failures = []
  try {
    mkdirSync(resolve(root, 'gallery/styles'), {recursive: true})
    writeFileSync(resolve(root, 'gallery/styles/canvas.css'), ':root{--hl-primary:#0f62fe;}')

    const plant = (id, css, tsx) => {
      mkdirSync(resolve(root, `specs/modules/ui/${id}`), {recursive: true})
      writeFileSync(resolve(root, `specs/modules/ui/${id}/style.css`), css)
      if (tsx !== undefined) {
        mkdirSync(resolve(root, `projections/react/ui/${id}/src`), {recursive: true})
        writeFileSync(resolve(root, `projections/react/ui/${id}/src/C.tsx`), tsx)
      }
    }
    plant('hlp.ui.canvas-ok', '.a{color:var(--hl-primary)}')
    plant('hlp.ui.self-ok', '.b{--hl-b-tone:#111;color:var(--hl-b-tone)}')
    plant('hlp.ui.runtime-ok', '.c{width:var(--hl-c-width)}', "style={{'--hl-c-width': w}}")
    plant('hlp.ui.fallback-warn', '.d{color:var(--hl-nope, #b42318)}')
    plant('hlp.ui.broken-fail', '.e{background:var(--hl-missing)}')

    const {findings, counts} = runScan(root)
    const verdict = id => findings.find(f => f.moduleId === id)?.severity ?? 'clean'
    const expected = {
      'hlp.ui.canvas-ok': 'clean',
      'hlp.ui.self-ok': 'clean',
      'hlp.ui.runtime-ok': 'clean',
      'hlp.ui.fallback-warn': 'warn',
      'hlp.ui.broken-fail': 'fail',
    }
    for (const [id, want] of Object.entries(expected)) {
      const got = verdict(id)
      if (got !== want) failures.push(`${id}: expected ${want}, got ${got}`)
    }
    if (counts.fail !== 1) failures.push(`expected exactly 1 fail, got ${counts.fail}`)
    if (counts.warn !== 1) failures.push(`expected exactly 1 warn, got ${counts.warn}`)
  } catch (error) {
    failures.push(`canary threw: ${error.message}`)
  } finally {
    try { rmSync(root, {recursive: true, force: true}) } catch {}
  }
  if (failures.length > 0) {
    process.stderr.write(`canary FAIL:\n${failures.map(f => `  ${f}`).join('\n')}\n`)
    process.exit(1)
  }
  process.stdout.write('canary OK -- canvas, module-local and runtime-set all read clean; fallback warns; undefined fails\n')
  process.exit(0)
}

const argv = process.argv.slice(2)
if (argv.includes('--canary')) canary()

const root = argv.find(a => !a.startsWith('--')) ?? defaultPlatformRoot
const report = runScan(root)

if (argv.includes('--json')) {
  process.stdout.write(`${JSON.stringify(report, null, 2)}\n`)
} else {
  process.stdout.write(`${report.canvasTokens} tokens defined in canvas.css\n\n`)
  process.stdout.write(`FAIL  ${report.counts.fail}  undefined with no fallback -- the declaration silently does not apply\n`)
  for (const f of report.findings.filter(f => f.severity === 'fail')) {
    process.stdout.write(`        ${f.moduleId.padEnd(28)} ${f.token}\n`)
  }
  process.stdout.write(`\nWARN  ${report.counts.warn}  resolves only through a var(--token, #literal) fallback\n`)
  const byToken = new Map()
  for (const f of report.findings.filter(f => f.severity === 'warn')) {
    byToken.set(f.token, (byToken.get(f.token) ?? 0) + 1)
  }
  for (const [token, n] of [...byToken].sort((a, b) => b[1] - a[1]).slice(0, 12)) {
    process.stdout.write(`        ${token.padEnd(30)} ${n} module${n === 1 ? '' : 's'}\n`)
  }
  if (byToken.size > 12) process.stdout.write(`        ... +${byToken.size - 12} more tokens\n`)
}

// A fallback renders, so it is not a build break. Only an undefined token with no fallback fails.
process.exitCode = report.counts.fail > 0 ? 1 : 0
