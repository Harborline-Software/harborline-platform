// Ticket 268. The quiet slot's two claims, tested where they can be tested without a gate:
// the CPU budget actually decides the signal, and the step is wired into the gate serially and
// unreusably. What the ceilings do with HARBORLINE_PERF_QUIET is the perf tests' own assertion.

import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import {resolve} from 'node:path'
import test from 'node:test'

import {requiredStepIds} from '../gate-contract.mjs'
import {reusableStepEntryScript} from '../gate-step-evidence.mjs'
import {QUIET_BUSY_BUDGET, busyFractionBetween, observeQuiet} from '../run-perf-budgets.mjs'

const root = resolve(import.meta.dirname, '../..')

test('busy fraction is busy time over elapsed CPU time, and zero when no time passed', () => {
  assert.equal(busyFractionBetween({idle: 100, busy: 100}, {idle: 200, busy: 100}), 0)
  assert.equal(busyFractionBetween({idle: 100, busy: 100}, {idle: 150, busy: 150}), 0.5)
  assert.equal(busyFractionBetween({idle: 100, busy: 100}, {idle: 100, busy: 300}), 1)
  assert.equal(busyFractionBetween({idle: 7, busy: 7}, {idle: 7, busy: 7}), 0)
})

test('the quiet signal follows the stated budget, not the wish', () => {
  // A budget of 0 cannot be met by a running host, and a budget of 1 always is: the decision is the
  // comparison, so both directions are reachable from one sample. Sampled briefly -- the property is
  // the branch, not the wall clock.
  assert.equal(observeQuiet(50, 0).quiet, false)
  const generous = observeQuiet(50, 1)
  assert.equal(generous.quiet, true)
  assert.ok(generous.busyFraction >= 0 && generous.busyFraction <= 1, `busy fraction out of range: ${generous.busyFraction}`)
  assert.equal(generous.budget, 1)
  assert.ok(QUIET_BUSY_BUDGET > 0 && QUIET_BUSY_BUDGET < 1, 'the default budget must be a fraction')
})

test('the gate runs the budgeted rows alone, after the parallel native step, and never reuses them', () => {
  const index = requiredStepIds.indexOf('perf-budgets')
  assert.notEqual(index, -1, 'the gate contract has no perf-budgets step')
  assert.equal(requiredStepIds[index - 1], 'native-tests', 'the quiet slot must follow the parallel native step')
  assert.equal(reusableStepEntryScript['perf-budgets'], undefined, 'a reused measurement is not a measurement')
  const gate = readFileSync(resolve(root, 'tooling/run-phase-4-gate.mjs'), 'utf8')
  assert.match(gate, /\n\s*run\('perf-budgets', process\.execPath, \['tooling\/run-perf-budgets\.mjs'\]/,
    'the step must be run(), not runReusable()')
})

test('nothing of the gate\'s own runs beside the budgeted rows: every layer spawns synchronously', () => {
  // The serial claim is not "the box is idle" -- the step measures the box and says so. It is that
  // the gate never has two of its own children in flight, so the rows never overlap the gate's
  // twenty-five parallel `dotnet test` projects. That holds because each of the three layers waits
  // for its child: the gate's step runner, this step's call into the stability tool, and the
  // stability tool's per-target invocation are all spawnSync, and none of them is spawn().
  for (const [file, runner] of [
    ['tooling/run-phase-4-gate.mjs', /function run\(id, executable, args[\s\S]*?const result = spawnSync\(/],
    ['tooling/run-perf-budgets.mjs', /const result = spawnSync\(process\.execPath, args,/],
    ['tooling/perf-budget-stability.mjs', /const result = spawnSync\(/],
  ]) {
    assert.match(readFileSync(resolve(root, file), 'utf8'), runner, `${file} must invoke its child synchronously`)
  }
  // The stability tool does import `spawn` -- for the burner processes, which exist to make the box
  // LOUD on purpose during a calibration run and are killed by pid inside the run. It must never be
  // how a measured target is started.
  const stability = readFileSync(resolve(root, 'tooling/perf-budget-stability.mjs'), 'utf8')
  for (const match of stability.matchAll(/(?<!spawnSync|\w)spawn\(/g)) {
    const line = stability.slice(0, match.index).split('\n').length
    const lines = stability.split('\n')
    // The enclosing block, not just the one line: `startBurners` names itself on its declaration.
    const context = lines.slice(Math.max(0, line - 6), line).join('\n')
    assert.match(context, /burner/i,
      `perf-budget-stability.mjs:${line} starts a child asynchronously outside the burner: ${lines[line - 1].trim()}`)
  }
})
