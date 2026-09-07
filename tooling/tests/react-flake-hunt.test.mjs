import assert from 'node:assert/strict'
import test from 'node:test'

import { summarizeRuns } from '../run-react-flake-hunt.mjs'

const result = (run, statuses) => ({
  moduleId: 'schema-form',
  run,
  assertions: Object.entries(statuses).map(([fullName, status]) => ({ fullName, status })),
})

test('reports per-test rates and distinguishes flakes from consistent failures', () => {
  const summary = summarizeRuns([
    result(1, { focus: 'passed', validation: 'failed' }),
    result(2, { focus: 'failed', validation: 'failed' }),
    result(3, { focus: 'passed', validation: 'failed' }),
    result(4, { focus: 'passed', validation: 'failed' }),
    result(5, { focus: 'failed', validation: 'failed' }),
  ], 5)

  assert.deepEqual(summary.infrastructureFailures, [])
  assert.deepEqual(summary.flaky.map(entry => [entry.test, entry.passed, entry.failed, entry.failureRate]), [
    ['focus', 3, 2, 0.4],
  ])
  assert.deepEqual(summary.consistentlyFailing.map(entry => entry.test), ['validation'])
})

test('fails closed when a test disappears from a run or a report cannot be read', () => {
  const summary = summarizeRuns([
    result(1, { focus: 'passed' }),
    result(2, {}),
    { moduleId: 'schema-form', run: 3, infrastructureFailure: 'schema-form run 3: missing report' },
  ], 3)

  assert.deepEqual(summary.infrastructureFailures, [
    'schema-form run 3: missing report',
    'schema-form :: focus appeared in 1/3 runs',
  ])
})
