import assert from 'node:assert/strict'
import {resolve} from 'node:path'
import test from 'node:test'

import {createGateModel} from '../gates/gate-rows.mjs'

const platformRoot = resolve(import.meta.dirname, '../..')

test('app-shell classifies its fixed minimum pane width as deterministic geometry', () => {
  const model = createGateModel(platformRoot)
  const determinism = model.gateRows(model.contextFor('hlp.ui.app-shell'))
    .find(row => row.id === 'assertDeterminism')

  assert.equal(determinism?.status, 'PASS')
  assert.equal(determinism?.note, 'cheap half only: no timing or async construct declared')
})
