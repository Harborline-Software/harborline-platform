#!/usr/bin/env node
// T-962: the static half of the tab-walk waiver register. gallery.spec.ts judges each scenario a run
// reaches against its rows (an unwaived difference or finding, a changed one and a row with nothing
// left to waive all fail there); a row no run can judge, or one too thin to review, is refused here.
import assert from 'node:assert/strict'
import {readFileSync, readdirSync} from 'node:fs'
import {resolve} from 'node:path'
import {test} from 'node:test'

// validate-gallery reads the repository relative to the working directory, as validate-repository
// and the gate's gallery-structure step both run it.
const root = resolve(import.meta.dirname, '../..')
process.chdir(root)
const {tabWalkRegisterErrors, validateGallery} = await import('../validate-gallery.mjs')

const flag = 'HARBORLINE_GALLERY_TAB_WALK_MEASURE'
const rule = 'Exact both ways.'
const order = overrides => ({
  scenarioId: 'switch.theme-light',
  kind: 'order',
  ticket: 'T-1104',
  reason: 'React names the switch from aria-label, Blazor from its visible label',
  hunks: ['@@ #1', '  - button "Save"', '- - switch "Notifications"', '+ - switch "Email notifications"', '  exited'],
  ...overrides,
})
const findings = overrides => ({
  scenarioId: 'switch.theme-light',
  kind: 'findings',
  ticket: 'T-1104',
  reason: 'The sticky header covers the last switch when Shift+Tab scrolls it up',
  reactOnly: ['2.4.11 reverse obscured: switch "Wi-Fi" under header.hl-page__header (sticky)'],
  blazorOnly: [],
  ...overrides,
})
const errorsFor = (...rows) => tabWalkRegisterErrors({rule, measureFlag: flag, divergences: rows}, ['switch.theme-light', 'switch.theme-dark'])

test('complete order and findings rows for a known scenario are accepted', () => {
  assert.deepEqual(errorsFor(order()), [])
  assert.deepEqual(errorsFor(findings()), [])
  assert.deepEqual(errorsFor(order(), findings()), [])
  // Host-scoped findings rows, one per host, are accepted together.
  assert.deepEqual(errorsFor(findings({host: 'macos-arm64'}), findings({host: 'linux-x64'})), [])
  // Every clause and direction token the walk writes.
  assert.deepEqual(errorsFor(findings({reactOnly: ['2.1.2 prepared stuck: textbox "Name"', '2.4.3 walk positive-tabindex: button "Go" tabindex=2'],
    blazorOnly: ['2.1.2 forward modal not released: dialog "Settings" (Escape ×2, Enter on 1 buttons)']})), [])
})

test('a row for an unknown scenario, kind or field fails', () => {
  assert.match(errorsFor(order({scenarioId: 'switch.retired'})).join('\n'), /waiver switch\.retired order names an unknown scenario id/)
  assert.match(errorsFor(order({kind: 'pixels'})).join('\n'), /has an unknown kind; it is order, findings or budget/)
  assert.match(errorsFor(order({note: 'x'})).join('\n'), /has an unknown field note/)
})

test('a host on an order row, or an unknown host, fails', () => {
  assert.match(errorsFor(order({host: 'macos-arm64'})).join('\n'), /is an order row, which applies on every host; it takes no host/)
  assert.match(errorsFor(findings({host: 'beos-x86'})).join('\n'), /names an unknown host/)
})

test('a duplicate row, or a host-less findings row beside host-scoped ones, fails', () => {
  assert.match(errorsFor(order(), order()).join('\n'), /switch\.theme-light order is registered twice for the same kind and host/)
  assert.match(errorsFor(findings({host: 'linux-x64'}), findings({host: 'linux-x64'})).join('\n'), /registered twice for the same kind and host/)
  assert.match(errorsFor(findings(), findings({host: 'linux-x64'})).join('\n'), /mixes a host-less findings row with host-scoped ones/)
})

test('a row without an open owning ticket, a reason or lines fails', () => {
  assert.match(errorsFor(order({ticket: undefined})).join('\n'), /needs the open ticket that owns the fix \(T-NNN\)/)
  assert.match(errorsFor(order({ticket: '962'})).join('\n'), /needs the open ticket that owns the fix/)
  assert.match(errorsFor(order({ticket: ''})).join('\n'), /needs the open ticket that owns the fix/)
  assert.match(errorsFor(order({ticket: 'T-943'})).join('\n'), /names T-943, the ticket that builds the ARIA-tree register, not a fix; name the open ticket that owns the fix/)
  // The gate's own ticket builds this register and closes with it, so it owns no row either.
  assert.match(errorsFor(findings({ticket: 'T-962'})).join('\n'), /names T-962, the ticket that builds this register and closes with it; name the open ticket that owns the fix/)
  assert.match(errorsFor(order({reason: ''})).join('\n'), /needs a reason a reviewer can check/)
  assert.match(errorsFor(findings({reactOnly: [], blazorOnly: []})).join('\n'), /records no finding line/)
  assert.match(errorsFor(findings({reactOnly: '2.4.11 forward obscured: x'})).join('\n'), /arrays of finding lines/)
  assert.match(errorsFor(findings({blazorOnly: [' ']})).join('\n'), /arrays of finding lines/)
  assert.match(errorsFor(findings({reactOnly: [3]})).join('\n'), /arrays of finding lines/)
})

test('an order row records hunks of walk lines as laneLineDiff writes them, and nothing else', () => {
  // Every walk line shape the order lines use is a valid hunk line.
  assert.deepEqual(errorsFor(order({hunks: ['@@ #1', '  ↻ - button "One"', '- ↺ ok', '+ ↺ differs', '- released by Escape ×1', '+ not released', '  exited']})), [])
  assert.deepEqual(errorsFor(order({hunks: ['@@ #1', '- - button "B"', '  exited', '@@ #2', '+ budget: 24 presses']})), [])
  assert.match(errorsFor(order({hunks: ['- - button "B"']})).join('\n'), /must record hunks as laneHunks writes them|records no differing walk line/)
  assert.match(errorsFor(order({hunks: ['@@ #1', '  - button "B"']})).join('\n'), /records no differing walk line/)
  // A bare '@@' is the pre-anchor header; laneHunks now numbers each hunk's first-line occurrence.
  assert.match(errorsFor(order({hunks: ['@@', '- - button "B"']})).join('\n'), /must record hunks as laneHunks writes them \('@@ #k'/)
  assert.match(errorsFor(order({hunks: ['@@ #1', 'line 2: - switch "Notifications"']})).join('\n'), /must record hunks as laneHunks writes them/)
  assert.match(errorsFor(order({hunks: ['@@ #1', '- ']})).join('\n'), /must record hunks as laneHunks writes them/)
  assert.match(errorsFor(order({hunks: undefined})).join('\n'), /must record hunks as laneHunks writes them/)
  assert.match(errorsFor(order({reactOnly: ['- switch "Notifications"']})).join('\n'), /is an order row; it records hunks, not reactOnly or blazorOnly/)
  assert.match(errorsFor(findings({hunks: ['@@ #1', '- - x']})).join('\n'), /is a findings row; it records reactOnly and blazorOnly, not hunks/)
})

test('a wall-clock deadline is never waived, and a budget row records a press budget and its press cost', () => {
  assert.match(errorsFor(order({hunks: ['@@ #1', '- - button "B"', '+ deadline']})).join('\n'), /waives a wall-clock deadline, which depends on the host; give the scenario a budget row instead/)
  assert.match(errorsFor(findings({blazorOnly: ['2.1.2 forward deadline: the walk did not finish within the test\'s allowance']})).join('\n'), /waives a wall-clock deadline/)
  assert.match(errorsFor(findings({reactOnly: ['2.1.2 walk deadline: covers were left unverified at the test\'s allowance']})).join('\n'), /waives a wall-clock deadline/)
  const budget = overrides => ({scenarioId: 'switch.theme-light', kind: 'budget', ticket: 'T-146', reason: 'The Blazor lane spends about 380 ms on each Tab press', presses: 24, pressMs: 400, ...overrides})
  assert.deepEqual(errorsFor(budget()), [])
  assert.deepEqual(errorsFor(budget(), order(), findings()), [])
  assert.match(errorsFor(budget({presses: 4})).join('\n'), /must give presses, a whole number of Tab presses from 8 to 200/)
  assert.match(errorsFor(budget({pressMs: undefined})).join('\n'), /must give pressMs/)
  assert.match(errorsFor(budget({hunks: ['@@ #1', '- - x']})).join('\n'), /is a budget row; it records presses and pressMs, not lines/)
  assert.match(errorsFor(budget({host: 'linux-x64'})).join('\n'), /is a budget row, which applies on every host; it takes no host/)
  assert.match(errorsFor(budget({ticket: 'T-962'})).join('\n'), /names T-962/)
  assert.match(errorsFor(budget(), budget()).join('\n'), /budget is registered twice/)
  assert.match(errorsFor(order({presses: 24})).join('\n'), /records presses or pressMs, which only a budget row takes/)
})

test('a findings line that does not start with its clause and direction fails', () => {
  assert.match(errorsFor(findings({reactOnly: ['obscured: switch "Wi-Fi"']})).join('\n'), /does not start with its clause and direction: "obscured: switch \\"Wi-Fi\\""/)
  assert.match(errorsFor(findings({reactOnly: ['2.4.7 forward obscured: x']})).join('\n'), /does not start with its clause and direction/)
  assert.match(errorsFor(findings({reactOnly: ['2.4.11 sideways obscured: x']})).join('\n'), /does not start with its clause and direction/)
})

test('a wrong measure flag, a missing rule or a register that is not a list fails', () => {
  assert.match(tabWalkRegisterErrors({rule, measureFlag: 'X', divergences: []}, []).join('\n'), /measureFlag must be HARBORLINE_GALLERY_TAB_WALK_MEASURE/)
  assert.match(tabWalkRegisterErrors({measureFlag: flag, divergences: []}, []).join('\n'), /rule must state the register's rule/)
  assert.match(tabWalkRegisterErrors({}, []).join('\n'), /divergences must be an array/)
})

test('the checked-in register is valid against the real catalogs, and validate-gallery reports it', () => {
  const scenarioIds = readdirSync(resolve(root, 'gallery/scenarios')).filter(name => /^hlp\.ui\..+\.json$/.test(name))
    .flatMap(name => JSON.parse(readFileSync(resolve(root, 'gallery/scenarios', name), 'utf8')).scenarios.map(scenario => scenario.id))
  const register = JSON.parse(readFileSync(resolve(root, 'gallery/tab-walk-known-divergences.json'), 'utf8'))
  assert.deepEqual(tabWalkRegisterErrors(register, scenarioIds), [])
  assert.deepEqual(validateGallery().errors.filter(error => /tab-walk|tab walk/i.test(error)), [])
})
