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
