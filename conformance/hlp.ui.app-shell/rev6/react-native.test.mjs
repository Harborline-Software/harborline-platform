import test from 'node:test'
import assert from 'node:assert/strict'
import { sameOwner, tabFocusIndex } from '../../../projections/react/ui/hlp.ui.app-shell/src/rev6/presentation-boundary.mjs'
import { runScenario, presentation, transition } from '../../../projections/shared/ui/hlp.ui.app-shell/rev6/tab-identity-state.mjs'

const alice = { person: 'alice', tenant: 'north', workspace: 'work' }
test('React boundary fails closed on each changed ownership axis and sign-out', () => {
  assert.equal(sameOwner(alice, { ...alice }), true)
  for (const axis of ['person', 'tenant', 'workspace']) assert.equal(sameOwner({ ...alice, [axis]: 'other' }, alice), false)
  assert.equal(sameOwner(null, alice), false)
  assert.equal(sameOwner(alice, null), false)
})
test('tab keyboard focus wraps and supports Home and End without consuming unrelated keys', () => {
  assert.equal(tabFocusIndex('ArrowRight', 2, 3), 0)
  assert.equal(tabFocusIndex('ArrowLeft', 0, 3), 2)
  assert.equal(tabFocusIndex('Home', 2, 3), 0)
  assert.equal(tabFocusIndex('End', 0, 3), 2)
  assert.equal(tabFocusIndex('Tab', 0, 3), -1)
  assert.equal(tabFocusIndex('Home', 0, 0), -1)
})
test('host Save intent leaves editor open until separate matching acknowledgment', () => {
  const id = '["Details","record-a"]'
  let state = runScenario([{ type: 'identity', owner: alice }, { type: 'open', kind: 'Details', target: 'record-a', preview: true }, { type: 'edit', id, draft: 'note' }, { type: 'close', id }, { type: 'resolve-close', choice: 'save' }])
  assert.equal(presentation(state).tabs.length, 1)
  assert.equal(presentation(state).tabs[0].dirty, true)
  const request = presentation(state).saveRequest
  assert.deepEqual(request, { owner: alice, id, generation: 1, requestId: 1 })
  state = transition(state, { type: 'saved', ...request })
  assert.equal(presentation(state).tabs.length, 0)
})
