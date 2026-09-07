// The canary's own canary. `run-vertical-pair.mjs --canary` is the detector for the gate model's
// VOID propagation and for ticket 138's surface-bound design-review rows, and it was DEAD: its
// context was a hand-written object, assertEmptyAndErrorStates started reading a field that object
// did not have, and the run threw at gate-rows.mjs before its first assertion. No test looked, so
// nothing was red for it. These tests are that look.
import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import path from 'node:path'
import test from 'node:test'

import {createGateModel} from '../gates/gate-rows.mjs'
import {CANARY_MODULE, CANARY_ROWS, canaryContexts, runCanary} from '../gates/vertical-pair-canary.mjs'

const repositoryRoot = path.resolve(import.meta.dirname, '../..')
const model = createGateModel(repositoryRoot)

// The schema the gate rows read is not written down anywhere; the only honest statement of it is
// the context the report itself is judged on. So the canary context must carry exactly that key
// set -- no field missing (the crash) and no field invented (a shape that drifts from the real one).
test('the canary context carries every field the real gate-row context does', () => {
  const {failing, clean} = canaryContexts(model)
  const real = Object.keys(model.contextFor(CANARY_MODULE)).sort()
  assert.deepEqual(Object.keys(failing).sort(), real)
  assert.deepEqual(Object.keys(clean).sort(), real)
})

// Key parity alone would pass a context whose values make every gate throw, so run the rows too.
test('every canary row is exercised and reports its expected verdict', () => {
  assert.deepEqual(runCanary(model), [])
  assert.ok(CANARY_ROWS.length >= 8, 'the named row list must not shrink silently')
})

test('the --canary entry point exits 0 and names the rows it exercised', () => {
  const result = spawnSync(process.execPath,
    [path.join(repositoryRoot, 'tooling/gates/run-vertical-pair.mjs'), repositoryRoot, '--canary'],
    {encoding: 'utf8'})
  assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`)
  for (const row of CANARY_ROWS) assert.ok(result.stdout.includes(row), `row not reported: ${row}`)
  // The rows ticket 138 slice 1 added, named explicitly: they are the ones the crash killed.
  assert.match(result.stdout, /surface-bound approval whose files are unchanged PASSes/)
  assert.match(result.stdout, /surface-bound verdict whose file changed FAILs and names ONLY that file/)
})
