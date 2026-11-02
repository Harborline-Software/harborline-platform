#!/usr/bin/env node
// T-943: the static half of the ARIA-tree waiver register. gallery.spec.ts judges each scenario a
// run reaches against its row (an unwaived difference, a changed difference and a now-matching row
// all fail there); a row that names no scenario is reached by no run, so validate-gallery refuses it.
import assert from 'node:assert/strict'
import {readFileSync, readdirSync} from 'node:fs'
import {resolve} from 'node:path'
import {test} from 'node:test'

// validate-gallery reads the repository relative to the working directory, as validate-repository
// and the gate's gallery-structure step both run it.
const root = resolve(import.meta.dirname, '../..')
process.chdir(root)
const {ariaParityRegisterErrors, validateGallery} = await import('../validate-gallery.mjs')

const flag = 'HARBORLINE_GALLERY_ARIA_MEASURE'
const hunks = ['@@ #1', '  - heading "Pseudo locale" [level=2]', '- - switch "Notifications"', '+ - switch "Email notifications"', '  - text: Email notifications']
const row = overrides => ({
  scenarioId: 'switch.theme-light',
  ticket: 'T-1111',
  reason: 'React names the switch from aria-label, Blazor from its visible label',
  hunks,
  ...overrides,
})
const errorsFor = (...rows) => ariaParityRegisterErrors({measureFlag: flag, divergences: rows}, ['switch.theme-light', 'switch.theme-dark'])

test('a complete row for a known scenario is accepted', () => {
  assert.deepEqual(errorsFor(row()), [])
  assert.deepEqual(errorsFor(row({ticket: 'T-146', hunks: ['@@ #2', '  - heading "Agenda" [level=2]', '- - toolbar:']})), [])
  // A later state of a timer-driven scene is its own row beside the prepared one.
  assert.deepEqual(errorsFor(row(), row({state: 'after-arm-window'})), [])
})

test('a waiver for an unknown scenario id fails', () => {
  assert.match(errorsFor(row({scenarioId: 'switch.retired'})).join('\n'), /waiver switch\.retired names an unknown scenario id/)
})

test('a waiver owned by T-943, the ticket that builds the register, fails', () => {
  // T-943 closes when the register lands, so a row naming it would have no open owner.
  assert.match(errorsFor(row({ticket: 'T-943'})).join('\n'), /names T-943, the ticket that builds this register and closes with it/)
  assert.match(errorsFor(row({ticket: '943'})).join('\n'), /needs the open ticket that owns the fix/)
})

test('a row without a ticket, a reason or differing lines fails', () => {
  assert.match(errorsFor(row({ticket: undefined})).join('\n'), /needs the open ticket that owns the fix/)
  assert.match(errorsFor(row({ticket: 'soon'})).join('\n'), /needs the open ticket that owns the fix/)
  assert.match(errorsFor(row({reason: 'differs'})).join('\n'), /needs a reason a reviewer can check/)
  assert.match(errorsFor(row({hunks: ['@@ #1', '  - heading "Pseudo locale" [level=2]']})).join('\n'), /records no differing snapshot line/)
  assert.match(errorsFor(row({hunks: ['@@', '- - switch "Notifications"']})).join('\n'), /as laneHunks writes them/)
  assert.match(errorsFor(row({hunks: []})).join('\n'), /records no differing snapshot line/)
  assert.match(errorsFor(row({hunks: '- - switch "Notifications"'})).join('\n'), /as laneHunks writes them/)
  assert.match(errorsFor(row({hunks: ['@@ #1', '- switch "Notifications"']})).join('\n'), /as laneHunks writes them/)
})

test('a scenario waived twice, an unknown field or a wrong measure flag fails', () => {
  assert.match(errorsFor(row(), row()).join('\n'), /switch\.theme-light is registered twice/)
  assert.match(errorsFor(row({state: 'later'}), row({state: 'later'})).join('\n'), /registered twice for state later/)
  assert.match(errorsFor(row({state: 'After Window'})).join('\n'), /state that is not a kebab-case name/)
  assert.match(errorsFor(row({host: 'macos-arm64'})).join('\n'), /unknown field host/)
  assert.match(errorsFor(row({reactOnly: ['- switch "Notifications"']})).join('\n'), /unknown field reactOnly/)
  assert.match(ariaParityRegisterErrors({measureFlag: 'X', divergences: []}, []).join('\n'), /measureFlag must be HARBORLINE_GALLERY_ARIA_MEASURE/)
  assert.match(ariaParityRegisterErrors({}, []).join('\n'), /divergences must be an array/)
})

test('the checked-in register is valid against the real catalogs, and validate-gallery reports it', () => {
  const scenarioIds = readdirSync(resolve(root, 'gallery/scenarios')).filter(name => /^hlp\.ui\..+\.json$/.test(name))
    .flatMap(name => JSON.parse(readFileSync(resolve(root, 'gallery/scenarios', name), 'utf8')).scenarios.map(scenario => scenario.id))
  const register = JSON.parse(readFileSync(resolve(root, 'gallery/aria-parity-known-divergences.json'), 'utf8'))
  assert.deepEqual(ariaParityRegisterErrors(register, scenarioIds), [])
  assert.deepEqual(validateGallery().errors.filter(error => /aria/i.test(error)), [])
})
