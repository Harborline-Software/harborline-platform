// The gallery catches these in seven minutes and a browser; the source shows them in under a second.
// Every rule here exists because a defect of that exact shape cost a full gate cycle on 2026-08-26:
// a foreground token absent from the registry falling to MarkText (3.19:1 black on red, 58 axe
// violations), the same fix then reading 2.28:1 in the dark theme because one literal cannot serve
// both, an SVG spinner inside role="status", and a module styled in one lane only.
import assert from 'node:assert/strict'
import test from 'node:test'
import {execFileSync} from 'node:child_process'
import {cpSync, mkdtempSync, readFileSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {join, resolve} from 'node:path'

const root = resolve(import.meta.dirname, '../..')
const scanner = resolve(root, 'tooling/gates/scan-style-hazards.mjs')

const run = args => {
  try {
    return {status: 0, out: execFileSync(process.execPath, [scanner, ...args], {cwd: root, encoding: 'utf8'})}
  } catch (error) {
    return {status: error.status ?? 1, out: `${error.stdout ?? ''}${error.stderr ?? ''}`}
  }
}

test('every rule fires on a fixture built to trip it', () => {
  const {status, out} = run(['--canary'])
  assert.equal(status, 0, out)
  assert.match(out, /canary OK/)
})

test('the repository carries no unacknowledged style hazard', () => {
  const {status, out} = run([])
  assert.equal(status, 0, out)
  assert.match(out, /0 unacknowledged style hazards/)
})

// Retired exemptions must disappear once the corresponding stylesheet is discoverable.
test('retired style-hazard exemptions are not reported', () => {
  const {out} = run([])
  assert.doesNotMatch(out, /ticket 140/)
  assert.doesNotMatch(out, /no-blazor-stylesheet/)
})

const listed = () => run(['--list']).out.trim().split('\n').filter(Boolean).map(line => JSON.parse(line))

// b19: page's latent GrayText text fallback. b20: the R5 findings outside the overlay modules, and
// the deliberate notification-center badge pair (R1), each named down to its selector. The set is
// pinned: it cannot grow or shrink silently.
test('the acknowledged hazards are exactly the listed selector-level entries', () => {
  const {status, out} = run([])
  assert.equal(status, 0, out)
  assert.match(out, /acknowledged, tracked by ticket \(20\)/)
  const findings = listed()
  assert.equal(findings.length, 20)
  const modules = [...new Set(findings.map(([, module]) => module.replace('hlp.ui.', '')))].sort()
  assert.deepEqual(modules, [
    'date-field', 'date-time-field', 'gantt', 'layers-rail', 'notification-center', 'page', 'radio-group',
    'schema-form', 'scroll-affordance', 'tooltip',
  ])
})

// b20 (WCAG 2.2 SC 1.4.11): no overlay module's focus ring or meaningful indicator falls to
// Highlight, SelectedItem, Mark or GrayText, acknowledged or not. The one overlay acknowledgement is
// the deliberate R1 badge pair, which is not an R5 finding.
test('overlay modules carry no non-text indicator finding, acknowledged or not', () => {
  const overlays = ['dialog', 'sheet', 'spotlight', 'window', 'popover', 'user-menu', 'notification-center', 'confirm-dialog']
  const overlayFindings = listed().filter(([, module]) => overlays.includes(module.replace('hlp.ui.', '')))
  assert.deepEqual(overlayFindings.map(([rule, module]) => `${rule}:${module}`),
    ['foreground-falls-to-system-colour:hlp.ui.notification-center'])
})

// b20 review: an acknowledgement covers one selector, not a module. The scanner runs on a copy of
// the spec stylesheets (--spec-root) so the repository is never touched.
const seeded = mutate => {
  const copy = mkdtempSync(join(tmpdir(), 'style-hazards-'))
  cpSync(resolve(root, 'specs/modules/ui'), copy, {recursive: true})
  mutate(copy)
  try { return run(['--spec-root', copy]) } finally { rmSync(copy, {recursive: true, force: true}) }
}
const append = (copy, module, css) => {
  const path = join(copy, module, 'style.css')
  writeFileSync(path, readFileSync(path, 'utf8') + css)
}
const replaceIn = (copy, module, from, to) => {
  const path = join(copy, module, 'style.css')
  const text = readFileSync(path, 'utf8')
  assert.ok(text.includes(from), `${module} style.css no longer contains ${from}`)
  writeFileSync(path, text.replace(from, to))
}

test('a new finding in an already-acknowledged module fails the scan', () => {
  const {status, out} = seeded(copy => append(copy, 'hlp.ui.radio-group',
    '\n.hl-radio-group__legend:focus-visible{outline:2px solid var(--hl-focus,Highlight)}\n'))
  assert.equal(status, 1, out)
  assert.match(out, /indicator-falls-to-low-contrast-system-colour \(1\)/)
  assert.match(out, /hl-radio-group__legend:focus-visible/)
})

test('a fixed acknowledged selector leaves a stale entry that fails the scan', () => {
  const {status, out} = seeded(copy => replaceIn(copy, 'hlp.ui.scroll-affordance',
    'var(--hl-focus, Highlight)', 'var(--hl-focus, CanvasText)'))
  assert.equal(status, 1, out)
  assert.match(out, /STALE EXEMPTION/)
  assert.match(out, /hl-scroll-affordance:focus-visible \{ outline \} \(declares 1, found 0\)/)
})

test('a partial fix that renames an acknowledged selector fails as stale and as live', () => {
  const {status, out} = seeded(copy => replaceIn(copy, 'hlp.ui.scroll-affordance',
    '.hl-scroll-affordance:focus-visible', '.hl-scroll-affordance:focus-within'))
  assert.equal(status, 1, out)
  assert.match(out, /STALE EXEMPTION/)
  assert.match(out, /hl-scroll-affordance:focus-within \{ outline: /)
})

test('an overlay regression to Highlight fails the scan', () => {
  const {status, out} = seeded(copy => replaceIn(copy, 'hlp.ui.notification-center',
    'background:var(--hl-danger,var(--hl-destructive,CanvasText))', 'background:var(--hl-danger,var(--hl-destructive,Mark))'))
  assert.equal(status, 1, out)
  assert.match(out, /hl-notification-center__badge \{ background: /)
})
