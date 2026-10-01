import assert from 'node:assert/strict'
import {existsSync, readFileSync, writeFileSync} from 'node:fs'
import test from 'node:test'
import {executeConformanceBatch, reconcileBatchResults} from '../dotnet-conformance-batch.mjs'

const fixtures = [{id: 'first'}, {id: 'second'}, {id: 'last'}]
const rows = fixtures.map(({id}) => ({caseId: id, passed: true}))
const outcome = {exitCode: 0, passedCount: 3, failedCount: 0}

test('batch evidence retains declared order and individual failures rather than stamping aggregate status', () => {
  const observed = [rows[2], {...rows[1], passed: false, failureOutput: 'literal oracle mismatch'}, rows[0]]
  const result = reconcileBatchResults(fixtures, observed, {exitCode: 1, passedCount: 2, failedCount: 1})
  assert.deepEqual(result.map(row => [row.caseId, row.passed]), [['first', true], ['second', false], ['last', true]])
  assert.equal(result[1].failureOutput, 'literal oracle mismatch')
})

test('aggregate counts and exit success cannot manufacture missing, duplicate, skipped or contradictory proof', () => {
  for (const observed of [rows.slice(0, 2), [rows[0], rows[0], rows[2]], [...rows, {caseId: 'extra', passed: true}], [rows[0], {...rows[1], passed: 'true'}, rows[2]]]) {
    assert.throws(() => reconcileBatchResults(fixtures, observed, outcome), /evidence/)
  }
  assert.throws(() => reconcileBatchResults(fixtures, rows, {...outcome, passedCount: 2}), /executed/)
  assert.throws(() => reconcileBatchResults(fixtures, rows, {...outcome, exitCode: 1}), /host failed/)
  assert.throws(() => reconcileBatchResults(fixtures, [rows[0], {...rows[1], passed: false}, rows[2]], outcome), /host succeeded/)
})

test('invocation uses a private fixture file, clears single-case state and removes temporary evidence after success or failure', () => {
  for (const skip of [false, true]) {
    let inputPath
    const invoke = () => executeConformanceBatch({moduleId: 'hlp.ui.pilot', fixtures, command: ['dotnet', 'test'], root: '.', execute(command, root, env) {
      inputPath = env.HARBORLINE_CONFORMANCE_BATCH
      assert.equal(env.HARBORLINE_CONFORMANCE_FIXTURE, '')
      assert.deepEqual(JSON.parse(readFileSync(inputPath, 'utf8')), {moduleId: 'hlp.ui.pilot', cases: fixtures})
      writeFileSync(env.HARBORLINE_CONFORMANCE_RESULTS, rows.map(row => JSON.stringify(row)).join('\n'))
      return {exitCode: 0, output: skip ? 'Passed: 2, Failed: 0, Skipped: 1' : 'Passed: 3, Failed: 0, Skipped: 0'}
    }})
    if (skip) assert.throws(invoke, /skipped/)
    else assert.deepEqual(invoke().rows, rows)
    assert.equal(existsSync(inputPath), false)
  }
})

test('an assertion failure cannot mask another case failing during xUnit teardown', () => {
  const fixtures = [{id: 'assertion-failed'}, {id: 'dispose-failed'}]
  const callbackRows = [
    {caseId: 'assertion-failed', passed: false, failureOutput: 'planted assertion failure'},
    {caseId: 'dispose-failed', passed: true},
  ]
  let inputPath
  assert.throws(() => executeConformanceBatch({moduleId: 'hlp.ui.teardown-probe', fixtures,
    command: ['dotnet', 'test'], root: '.', execute(command, root, env) {
      inputPath = env.HARBORLINE_CONFORMANCE_BATCH
      writeFileSync(env.HARBORLINE_CONFORMANCE_RESULTS, callbackRows.map(row => JSON.stringify(row)).join('\n'))
      return {exitCode: 1, output: 'Failed: 2, Passed: 0, Skipped: 0, Total: 2'}
    }}), /host outcomes.*0 passed, 2 failed.*case evidence.*1 passed, 1 failed/)
  assert.equal(existsSync(inputPath), false)
  // A pure assertion failure still keeps its case-specific diagnosis.
  const result = reconcileBatchResults(fixtures, callbackRows, {exitCode: 1, passedCount: 1, failedCount: 1})
  assert.equal(result[0].failureOutput, 'planted assertion failure')
  assert.equal(result[1].passed, true)
})
