import assert from 'node:assert/strict'
import {existsSync, mkdtempSync, readFileSync, rmSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve} from 'node:path'
import test from 'node:test'

import {interruptionDetails, OUTPUT_CAP, runPhase4Step, writeFailureEvidence} from '../phase4-step-runner.mjs'

function failureEvidence(root, id) {
  return {
    filePath: resolve(root, '.claude/gate-evidence', `${id}.log`),
    reportPath: `.claude/gate-evidence/${id}.log`,
  }
}

test('an oversized failed step preserves complete output outside the bounded receipt', () => {
  const root = mkdtempSync(resolve(tmpdir(), 'phase4-full-output-'))
  try {
    const results = []
    const output = `first\n${'x'.repeat(OUTPUT_CAP)}\nlast`
    assert.throws(() => runPhase4Step({
      results,
      id: 'oversized-output',
      executable: process.execPath,
      args: [],
      cwd: root,
      json: false,
      env: {},
      failureEvidence: failureEvidence(root, 'oversized-output'),
      execute: () => ({
        result: {status: 1, stdout: output, stderr: ''},
        outcome: {status: 1, report: {status: 'FAIL'}},
      }),
    }), /oversized-output failed/)
    assert.equal(readFileSync(resolve(root, results[0].failureEvidencePath), 'utf8'), output)
    const bounded = results[0].failureOutput
    assert.ok(bounded.length <= OUTPUT_CAP)
    assert.match(bounded, /x\nlast$/)
  } finally {
    rmSync(root, {recursive: true, force: true})
  }
})

test('a failed step carries a pointer to failure output that exists', () => {
  const root = mkdtempSync(resolve(tmpdir(), 'phase4-failure-pointer-'))
  try {
    const results = []
    assert.throws(() => runPhase4Step({
      results,
      id: 'pointer-step',
      executable: process.execPath,
      args: [],
      cwd: root,
      json: false,
      env: {},
      failureEvidence: failureEvidence(root, 'pointer-step'),
      execute: () => ({
        result: {status: 1, stdout: 'complete failure output', stderr: ''},
        outcome: {status: 1, report: {status: 'FAIL'}},
      }),
    }), /pointer-step failed/)
    // The pointer rides the in-memory report the operator message and the stdout
    // report are built from -- not docs/evidence/phase-4/gate.json, which stays a
    // PASS-only record so a red run keeps the previous pass's step-reuse baseline.
    assert.equal(results[0].failureEvidencePath, '.claude/gate-evidence/pointer-step.log')
    assert.equal(existsSync(resolve(root, results[0].failureEvidencePath)), true)
  } finally {
    rmSync(root, {recursive: true, force: true})
  }
})

test('a large JSON failure retains flaky-test diagnostics beyond the bounded tail', () => {
  const root = mkdtempSync(resolve(tmpdir(), 'phase4-json-failure-'))
  try {
    const results = []
    const diagnostics = {
      failingTests: ['gallery/MemberEdit.spec.ts: restores the saved member'],
      flakyTests: [{
        id: 'gallery/PersonSearch.spec.ts: filters results',
        retries: 2,
        error: 'expect(locator).toHaveCount(1) timed out',
      }],
      reportLocation: 'gallery/tests/test-results/results.json',
      scenarioCounts: Array.from({length: 250}, (_, index) => ({
        module: `hlp.ui.example-${index}`,
        scenarios: index + 1,
      })),
    }
    const stdout = JSON.stringify(diagnostics, null, 2)
    assert.ok(stdout.length > OUTPUT_CAP)
    // 120 is the tail run-gallery-gate.mjs keeps of its own sub-steps: the window the incident lost.
    const tail = stdout.split('\n').slice(-120).join('\n')
    assert.doesNotMatch(tail, /PersonSearch|restores the saved member|toHaveCount/)

    assert.throws(() => runPhase4Step({
      results,
      id: 'gallery-gate',
      executable: process.execPath,
      args: ['tooling/run-gallery-gate.mjs', '--packages-ready'],
      cwd: root,
      json: true,
      env: {},
      failureEvidence: failureEvidence(root, 'gallery-gate'),
      execute: () => ({
        result: {status: 1, stdout, stderr: ''},
        outcome: {status: 1, report: {status: 'FAIL'}},
      }),
    }), /gallery-gate failed/)

    const entry = results[0]
    assert.deepEqual(entry.command, [process.execPath, 'tooling/run-gallery-gate.mjs', '--packages-ready'])
    assert.equal(entry.exitCode, 1)
    assert.equal(entry.failureEvidencePath, '.claude/gate-evidence/gallery-gate.log')
    assert.deepEqual(JSON.parse(readFileSync(resolve(root, entry.failureEvidencePath), 'utf8')), diagnostics)
  } finally {
    rmSync(root, {recursive: true, force: true})
  }
})

test('a plain-log failure over the cap keeps its final diagnostic lines in the bounded report', () => {
  const root = mkdtempSync(resolve(tmpdir(), 'phase4-log-failure-'))
  try {
    const results = []
    const stdout = Array.from({length: 400}, (_, index) => `progress line ${index + 1} ${'.'.repeat(60)}`).join('\n')
    assert.ok(stdout.length > OUTPUT_CAP)
    const stderr = 'FATAL: package-consumers fixture installation failed'
    assert.throws(() => runPhase4Step({
      results,
      id: 'package-consumers',
      executable: 'pnpm',
      args: ['install', '--frozen-lockfile'],
      cwd: root,
      json: false,
      env: {},
      failureEvidence: failureEvidence(root, 'package-consumers'),
      execute: () => ({
        result: {status: 1, stdout, stderr},
        outcome: {status: 1, report: {status: 'FAIL'}},
      }),
    }), /package-consumers failed/)

    assert.match(results[0].failureOutput, /progress line 400 /)
    assert.ok(results[0].failureOutput.length <= OUTPUT_CAP)
    assert.match(results[0].failureOutput, /FATAL: package-consumers fixture installation failed/)
    assert.equal(readFileSync(resolve(root, results[0].failureEvidencePath), 'utf8'), `${stdout}\n${stderr}`)
  } finally {
    rmSync(root, {recursive: true, force: true})
  }
})

test('a failed launch with no output to copy does not throw while retaining evidence', () => {
  const root = mkdtempSync(resolve(tmpdir(), 'phase4-missing-output-'))
  try {
    const evidence = failureEvidence(root, 'missing-output')
    assert.doesNotThrow(() => writeFailureEvidence(evidence, undefined, null))
    assert.equal(existsSync(evidence.filePath), false)
    const results = []
    const launchError = new Error('launch failed before output')
    assert.throws(() => runPhase4Step({
      results,
      id: 'missing-output',
      executable: process.execPath,
      args: [],
      cwd: root,
      json: false,
      env: {},
      failureEvidence: evidence,
      execute: () => ({result: {status: null, stdout: undefined, stderr: null, error: launchError}}),
    }), /launch failed before output/)
    assert.equal(results[0].failureEvidencePath, undefined)
    assert.equal(existsSync(evidence.filePath), false)
  } finally {
    rmSync(root, {recursive: true, force: true})
  }
})

test('an ordinary non-zero child records exactly one failed result', () => {
  const results = []
  assert.throws(() => runPhase4Step({
    results,
    id: 'ordinary-nonzero',
    executable: process.execPath,
    args: ['tooling/run-perf-budgets.mjs'],
    cwd: process.cwd(),
    json: false,
    env: {},
    execute: () => ({
      result: {status: 7, stdout: 'ordinary output', stderr: 'ordinary error'},
      outcome: {status: 7, report: {status: 'FAIL'}},
    }),
  }), /ordinary-nonzero failed/)
  assert.equal(results.length, 1)
  assert.equal(results[0].id, 'ordinary-nonzero')
  assert.equal(results[0].passed, false)
  assert.equal(results[0].exitCode, 7)
})

test('an evaluation throw records exactly one failed result with its cause', () => {
  const results = []
  const evaluationError = Object.assign(new Error('stdout evaluation mutation'), {code: 'EVAL_MUTATION'})
  assert.throws(() => runPhase4Step({
    results,
    id: 'evaluation-throw',
    executable: process.execPath,
    args: ['tooling/run-perf-budgets.mjs'],
    cwd: process.cwd(),
    json: true,
    env: {},
    execute: () => {
      const execution = {result: {status: 0, stdout: '{"status":"PASS"}', stderr: ''}}
      Object.defineProperty(execution, 'outcome', {get: () => { throw evaluationError }})
      return execution
    },
  }), /stdout evaluation mutation/)
  assert.equal(results.length, 1)
  assert.equal(results[0].passed, false)
  assert.equal(results[0].exitCode, 1)
  assert.deepEqual(results[0].error, {
    message: 'stdout evaluation mutation',
    code: 'EVAL_MUTATION',
    status: 0,
  })
})

test('a spawnSync launch failure records perf-budgets before the gate fails', () => {
  const results = []
  const launchError = Object.assign(new Error('spawn node ENOMEM'), {code: 'ENOMEM', errno: -12})
  const spawnSync = () => ({error: launchError, status: null, stdout: null, stderr: 'launch exhausted'})
  assert.throws(() => runPhase4Step({
    results,
    id: 'perf-budgets',
    executable: process.execPath,
    args: ['tooling/run-perf-budgets.mjs'],
    cwd: process.cwd(),
    json: true,
    env: {},
    execute: () => ({result: spawnSync()})
  }), /ENOMEM/)
  const report = {status: results.every(result => result.passed) ? 'PASS' : 'FAIL', results}
  assert.equal(report.status, 'FAIL')
  assert.deepEqual(report.results, [{
    id: 'perf-budgets',
    command: [process.execPath, 'tooling/run-perf-budgets.mjs'],
    exitCode: null,
    durationMs: results[0].durationMs,
    passed: false,
    error: {message: 'spawn node ENOMEM', code: 'ENOMEM', errno: -12, status: null},
    stdout: '',
    stderr: 'launch exhausted',
    failureOutput: 'launch exhausted\nspawn node ENOMEM',
  }])
})

test('an early stop names its failed step and every required step not run', () => {
  const required = ['native-tests', 'perf-budgets', 'ui-shared-conformance', 'package-consumers']
  const interruption = interruptionDetails([
    {id: 'native-tests', passed: true},
    {id: 'perf-budgets', passed: false},
  ], required)
  assert.deepEqual(interruption, {
    stoppedAt: 'perf-budgets',
    unreachedRequiredStepIds: ['ui-shared-conformance', 'package-consumers'],
  })
})
