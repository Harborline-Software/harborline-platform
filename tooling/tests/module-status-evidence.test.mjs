// assertGateCanFail, applied to the validator rather than to a scanner.
//
// `implemented-gate-model-green` is the only module status that is a CLAIM about evidence rather
// than a label. A check on such a claim that nobody has watched fail is not a check, so each of
// these cases is a way the claim could escape its evidence, and each must be refused.

import assert from 'node:assert/strict'
import test from 'node:test'

import {moduleStatusErrors} from '../validator-policy.mjs'

const RECEIPT = 'docs/evidence/gate-model/ui-gate-model.json'
const terminalRow = {terminal: true, gates: [{id: 'assertAccessible', status: 'PASS'}]}
const blockedRow = {
  terminal: false,
  gates: [
    {id: 'assertAccessible', status: 'PASS'},
    {id: 'assertDesignReview', status: 'UNBUILT'},
    {id: 'assertFocusQuality', status: 'FAIL'},
  ],
}

test('a status outside the vocabulary is refused', () => {
  const errors = moduleStatusErrors('hlp.ui.x', 'shipped-probably', {}, RECEIPT)
  assert.equal(errors.length, 1)
  assert.match(errors[0], /unknown module status shipped-probably/)
})

test('a status that is not a claim about evidence is not checked against it', () => {
  // extracted-candidate says nothing about gates, so the receipt is irrelevant to it. Checking it
  // anyway would make every module fail for a claim it never made.
  assert.deepEqual(moduleStatusErrors('hlp.ui.x', 'extracted-candidate', null, RECEIPT), [])
})

test('a terminal row supports the claim', () => {
  assert.deepEqual(moduleStatusErrors('hlp.ui.x', 'implemented-gate-model-green',
    {'hlp.ui.x': terminalRow}, RECEIPT), [])
})

test('a non-terminal row refuses the claim and names the gates that contradict it', () => {
  const errors = moduleStatusErrors('hlp.ui.x', 'implemented-gate-model-green',
    {'hlp.ui.x': blockedRow}, RECEIPT)
  assert.equal(errors.length, 1)
  // Naming the blocking gates is the point: the single historic message that blamed the tree for
  // every mismatch cost a wasted resume when the real cause was elsewhere.
  assert.match(errors[0], /assertDesignReview=UNBUILT/)
  assert.match(errors[0], /assertFocusQuality=FAIL/)
  assert.doesNotMatch(errors[0], /assertAccessible/)
})

test('a module absent from the receipt cannot claim the status', () => {
  const errors = moduleStatusErrors('hlp.ui.missing', 'implemented-gate-model-green', {}, RECEIPT)
  assert.equal(errors.length, 1)
  assert.match(errors[0], /no row for it/)
})

test('an absent receipt fails closed rather than open', () => {
  // The dangerous direction. If a missing receipt read as "nothing to check", then deleting it --
  // or cloning fresh, before the gate has run once -- would silently bless every claim in the
  // catalog. Both null and undefined arrive here in practice.
  for (const rows of [null, undefined]) {
    const errors = moduleStatusErrors('hlp.ui.x', 'implemented-gate-model-green', rows, RECEIPT)
    assert.equal(errors.length, 1)
    assert.match(errors[0], /is absent, so the claim cannot be checked/)
  }
})
