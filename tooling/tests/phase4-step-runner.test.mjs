import assert from 'node:assert/strict'
import test from 'node:test'

import {interruptionDetails, runPhase4Step} from '../phase4-step-runner.mjs'

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
