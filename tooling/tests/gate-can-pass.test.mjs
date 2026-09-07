// assertGateCanPass — the mirror of assertGateCanFail.
//
// A gate that can never FAIL is dead, and this repository has caught that three times. A gate that
// can never PASS is dead in the other direction, and nothing was watching for it: five Tier-1 gates
// could only ever return PARTIAL or UNBUILT, a module is terminal only on PASS or NOT-APPLICABLE,
// so fifty-nine visual modules were structurally unreachable. The blocked-by-gate table showed 59
// against each and read like a backlog rather than a wall.
//
// A ceiling is not a defect — each is an honest refusal to claim more than the evidence supports.
// An UNDECLARED ceiling is. This test fails in both directions, which is the point: lifting a gate
// without removing its entry fails, and capping one without adding an entry fails too.

import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import {resolve} from 'node:path'
import test from 'node:test'

import {createGateModel} from '../gates/gate-rows.mjs'

const root = resolve(import.meta.dirname, '../..')
const {GATES, TIER1_GATE_IDS, PARTIAL_CEILINGS} = createGateModel(root)

// Read the source rather than execute every gate against synthetic inputs for all six statuses:
// the verdicts close over scanners and recorded evidence, and a fixture rich enough to drive them
// all would be a second implementation of contextFor. What is being asserted is narrow and static —
// does this gate's verdict have a PASS branch at all — and source is where that lives.
const source = readFileSync(resolve(root, 'tooling/gates/gate-rows.mjs'), 'utf8')
// Mapped per gate, not concatenated. An earlier draft appended BOTH helpers to every delegating
// gate, so design-review.mjs's PASS leaked into assertStateCompleteness and the audit reported a
// ceiling it could not see. This test failing on its own wiring is what caught it.
const helperFor = {
  assertStateCompleteness: 'derive-state-set.mjs',
  assertDesignReview: 'design-review.mjs',
  assertDesignQuality: 'design-review.mjs',
}
const helperSource = name => readFileSync(resolve(root, 'tooling/gates', name), 'utf8')

function verdictSourceFor(gateId) {
  const start = source.indexOf(`id: '${gateId}'`)
  assert.notEqual(start, -1, `${gateId} not found in gate-rows.mjs`)
  const index = TIER1_GATE_IDS.indexOf(gateId)
  const next = index + 1 < TIER1_GATE_IDS.length
    ? source.indexOf(`id: '${TIER1_GATE_IDS[index + 1]}'`)
    : source.length
  // A delegating gate's verdict lives in ITS helper, so exactly that one is appended -- never all
  // of them, or a sibling's PASS is read as this gate's.
  const body = source.slice(start, next)
  const helper = helperFor[gateId]
  return helper ? body + String.fromCharCode(10) + helperSource(helper) : body
}

test('every Tier-1 gate either has a PASS branch or a declared ceiling', () => {
  const undeclared = []
  for (const gate of GATES) {
    const canPass = verdictSourceFor(gate.id).includes("'PASS'")
    if (!canPass && !PARTIAL_CEILINGS[gate.id]) undeclared.push(gate.id)
  }
  assert.deepEqual(undeclared, [],
    `these gates can never return PASS and do not declare why; a module is terminal only on PASS or NOT-APPLICABLE, so each one silently makes every module it applies to unreachable`)
})

test('a declared ceiling is removed when the gate gains a PASS branch', () => {
  const stale = []
  for (const gateId of Object.keys(PARTIAL_CEILINGS)) {
    if (verdictSourceFor(gateId).includes("'PASS'")) stale.push(gateId)
  }
  assert.deepEqual(stale, [],
    `these gates CAN now return PASS but still declare a ceiling; a stale ceiling understates what the suite proves, which is the same disease as an overstated one`)
})

test('every declared ceiling says what would lift it', () => {
  for (const [gateId, reason] of Object.entries(PARTIAL_CEILINGS)) {
    assert.ok(reason.length > 80, `${gateId}: a ceiling needs a reason someone can act on, not a label`)
    assert.match(reason, /needs|lifting it/i, `${gateId}: the reason must name what would lift the ceiling`)
  }
})

test('the ceilings name gates that actually exist', () => {
  for (const gateId of Object.keys(PARTIAL_CEILINGS)) {
    assert.ok(TIER1_GATE_IDS.includes(gateId), `${gateId} is not a Tier-1 gate; a ceiling on nothing is noise`)
  }
})
