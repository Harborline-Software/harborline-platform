// assertAccessible counts only what the recorded gallery-gate scanned. The gate is recorded against
// one tested tree; a scenario merged after it was never drawn or axe-scanned, so a note counting the
// working-tree catalog overstates the evidence (platform #424, CodeRabbit "Bind accessibility counts
// to the recorded gate"). These cases fail if the count goes back to the working-tree catalog.

import assert from 'node:assert/strict'
import {resolve} from 'node:path'
import test from 'node:test'

import {createGateModel} from '../gates/gate-rows.mjs'

const root = resolve(import.meta.dirname, '../..')
const model = createGateModel(root)
const accessible = model.verdictOf('assertAccessible')
const catalog = ids => ({scenarios: ids.map(id => ({id}))})

test('every scenario in the recorded tree: PASS over exactly those scenarios', () => {
  const [status, note] = accessible({gallery: true, catalog: catalog(['a', 'b']), recordedScenarios: new Set(['a', 'b'])})
  assert.equal(status, 'PASS')
  assert.equal(note, 'axe-core over 2 scenarios x 2 lanes in gallery-gate')
})

test('a scenario added after the recorded gate is not counted as scanned and is named', () => {
  const [status, note] = accessible({gallery: true, catalog: catalog(['a', 'b', 'a.inside-dialog']), recordedScenarios: new Set(['a', 'b'])})
  assert.equal(status, 'PARTIAL')
  assert.match(note, /^axe-core over 2 scenarios x 2 lanes in the recorded gallery-gate; 1 added since/)
  assert.match(note, /a\.inside-dialog$/)
})

test('an unreadable recorded tree counts nothing', () => {
  const [status] = accessible({gallery: true, catalog: catalog(['a']), recordedScenarios: null})
  assert.equal(status, 'PARTIAL')
})

test('no recorded gallery-gate stays UNBUILT', () => {
  assert.equal(accessible({gallery: false, catalog: catalog(['a']), recordedScenarios: new Set(['a'])})[0], 'UNBUILT')
})

test('this repository: the overlay scenarios merged after the recorded gate are reported unscanned', () => {
  const tree = model.gate?.subject?.testedTree
  const ctx = model.contextFor('hlp.ui.user-menu')
  assert.ok(ctx.recordedScenarios, `recorded tree ${tree} must be readable in a full clone`)
  const added = ctx.catalog.scenarios.map(s => s.id).filter(id => !ctx.recordedScenarios.has(id))
  const [status, note] = accessible(ctx)
  assert.equal(status, added.length === 0 ? 'PASS' : 'PARTIAL')
  for (const id of added) assert.ok(note.includes(id), `${id} must be named in: ${note}`)
})

// The visual-parity and reflow rows read the same recorded gallery-gate, so they are bound to its
// tested tree the same way (platform #497, Terra "gallery rows claim a run that was not recorded").
// A parity or reflow scenario merged after the gate was recorded was never compared or measured.
const parity = model.verdictOf('assertVisualParity')
const reflow = model.verdictOf('assertResponsiveQuality')
const withCase = (ids, suffix) => ({scenarios: ids.map(id => ({id, sourceQualityCaseIds: [`m.${id}${suffix}`]}))})

test('visual parity: every parity scenario in the recorded tree is PASS', () => {
  const [status, note] = parity({gallery: true, catalog: withCase(['a'], '.quality.visual-parity'), recordedScenarios: new Set(['a'])})
  assert.equal(status, 'PASS')
  assert.equal(note, '1 visual-parity scenario(s), lane vs lane, pixelmatch')
})

test('visual parity: a parity scenario added after the recorded gate is PARTIAL and named', () => {
  const [status, note] = parity({gallery: true, catalog: withCase(['a', 'b'], '.quality.visual-parity'), recordedScenarios: new Set(['a'])})
  assert.equal(status, 'PARTIAL')
  assert.equal(note, '1 visual-parity scenario(s) compared in the recorded gallery-gate; 1 added since it was recorded are uncompared until it is re-recorded: b')
})

test('visual parity: an unreadable recorded tree is PARTIAL', () => {
  assert.equal(parity({gallery: true, catalog: withCase(['a'], '.quality.visual-parity'), recordedScenarios: null})[0], 'PARTIAL')
})

test('reflow: a drawn reflow scenario in the recorded tree is PASS', () => {
  const [status] = reflow({gallery: true, rendersReflow: true, declaresReflow: true, catalog: withCase(['a'], '.quality.reflow'), recordedScenarios: new Set(['a'])})
  assert.equal(status, 'PASS')
})

test('reflow: a reflow scenario added after the recorded gate is PARTIAL and named', () => {
  const [status, note] = reflow({gallery: true, rendersReflow: true, declaresReflow: true, catalog: withCase(['a'], '.quality.reflow'), recordedScenarios: new Set()})
  assert.equal(status, 'PARTIAL')
  assert.equal(note, 'reflow is unmeasured for 1 scenario(s) added since the recorded gallery-gate, until it is re-recorded: a')
})

test('reflow: an unreadable recorded tree is PARTIAL', () => {
  assert.equal(reflow({gallery: true, rendersReflow: true, declaresReflow: true, catalog: withCase(['a'], '.quality.reflow'), recordedScenarios: null})[0], 'PARTIAL')
})

test('this repository: the schedule editor scenarios are newer than the recorded gate, so parity and reflow are PARTIAL', () => {
  const ctx = model.contextFor('hlp.ui.scheduling-authoring')
  assert.equal(parity(ctx)[0], 'PARTIAL')
  assert.equal(reflow(ctx)[0], 'PARTIAL')
})
