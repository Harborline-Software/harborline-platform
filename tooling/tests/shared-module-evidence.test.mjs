// A failed ui-shared-conformance module must name its failing cases. On platform PR 181 the
// evidence kept only the last 80 lines of the module's pretty-printed report, and every case in
// that tail read "passed": true: the failing case sat above the cut.

import assert from 'node:assert/strict'
import {test} from 'node:test'

import {CASE_OUTPUT_LINES, moduleEvidence} from '../shared-module-evidence.mjs'

function passingCase(index) {
  return {
    moduleId: 'hlp.ui.data-grid',
    caseId: `data-grid-case-${String(index).padStart(3, '0')}`,
    projection: 'blazor-support',
    command: ['dotnet', 'test', '--filter', 'ModuleConformance=hlp.ui.data-grid'],
    testCount: 1,
    exitCode: 0,
    passed: true,
  }
}

function syntheticReport() {
  const caseOutput = Array.from({length: 120}, (_, line) => `dotnet line ${line}`)
  caseOutput.push('Failed DataGridPerformanceTests.SortsTenThousandRows: 812ms exceeded the 500ms budget')
  const results = [
    {...passingCase(0), caseId: '(react-suite)', projection: 'react'},
    {
      ...passingCase(1),
      caseId: 'data-grid-sort-large-dataset',
      exitCode: 1,
      testCount: 0,
      passed: false,
      failureOutput: caseOutput.join('\n'),
    },
    ...Array.from({length: 200}, (_, index) => passingCase(index + 2)),
  ]
  return {
    schemaVersion: 1,
    moduleId: 'hlp.ui.data-grid',
    status: 'FAIL',
    counts: {interfaceCases: 201, projections: 2, expectedResults: 202, executedResults: 202, passedResults: 201},
    results,
  }
}

function execution(overrides = {}) {
  return {
    id: 'hlp.ui.data-grid',
    exitCode: 1,
    durationMs: 4200,
    stdout: `${JSON.stringify(syntheticReport(), null, 2)}\n`,
    stderr: 'test host exited with 0xC0000005\n',
    ...overrides,
  }
}

test('the synthetic failure sits outside the old 80-line tail', () => {
  const tailed = execution().stdout.trimEnd().split('\n').slice(-80).join('\n')
  assert.doesNotMatch(tailed, /data-grid-sort-large-dataset/)
})

test('a failed module names its failing case, command, exit code and that case output tail', () => {
  const evidence = moduleEvidence({...execution(), attempts: 1}, false)
  const recorded = JSON.parse(evidence.failureOutput)
  assert.equal(recorded.exitCode, 1)
  assert.equal(recorded.reportStatus, 'FAIL')
  assert.deepEqual(recorded.failures.map(entry => entry.caseId), ['data-grid-sort-large-dataset'])
  const [failure] = recorded.failures
  assert.equal(failure.projection, 'blazor-support')
  assert.equal(failure.exitCode, 1)
  assert.deepEqual(failure.command, ['dotnet', 'test', '--filter', 'ModuleConformance=hlp.ui.data-grid'])
  assert.match(failure.outputTail, /SortsTenThousandRows: 812ms exceeded the 500ms budget/)
  assert.equal(failure.outputTail.split('\n').length, CASE_OUTPUT_LINES)
  assert.match(recorded.stderrTail, /0xC0000005/)
  assert.equal(recorded.stdoutTail, undefined, 'a report that names its failures needs no raw tail')
  assert.equal(evidence.counts.passedResults, 201)
  assert.match(evidence.failureOutput.split('\n').slice(0, 20).join('\n'), /data-grid-sort-large-dataset/,
    'the gate failure report prints the first 20 lines; the failing case must be among them')
})

test('a retried module keeps the first attempt, including when the retry passed', () => {
  const first = execution({durationMs: 5100})
  const failedTwice = moduleEvidence({...execution(), attempts: 2, firstAttempt: first}, false)
  assert.equal(failedTwice.attempts, 2)
  assert.deepEqual(failedTwice.firstAttempt.failures.map(entry => entry.caseId), ['data-grid-sort-large-dataset'])
  assert.equal(failedTwice.firstAttempt.durationMs, 5100)
  assert.match(failedTwice.firstAttempt.stderrTail, /0xC0000005/)

  const passingReport = {...syntheticReport(), status: 'PASS', results: [passingCase(0)]}
  const clearedOnRetry = moduleEvidence({
    ...execution({exitCode: 0, stderr: '', stdout: JSON.stringify(passingReport)}),
    attempts: 2,
    firstAttempt: first,
  }, true)
  assert.equal(clearedOnRetry.failureOutput, undefined)
  assert.deepEqual(clearedOnRetry.firstAttempt.failures.map(entry => entry.caseId), ['data-grid-sort-large-dataset'])
})

test('a module that printed no report falls back to its raw stdout and stderr tails', () => {
  const recorded = JSON.parse(moduleEvidence({
    ...execution({stdout: 'Data Grid Blazor JavaScript conformance failed\nvitest: 1 failed', stderr: 'Error: boom\n    at run'}),
    attempts: 1,
  }, false).failureOutput)
  assert.deepEqual(recorded.failures, [])
  assert.equal(recorded.reportStatus, undefined)
  assert.match(recorded.stdoutTail, /vitest: 1 failed/)
  assert.match(recorded.stderrTail, /Error: boom/)
})

test('a passing module that ran once records no failure evidence', () => {
  assert.deepEqual(moduleEvidence({...execution({exitCode: 0, stderr: ''}), attempts: 1}, true), {attempts: 1})
})
