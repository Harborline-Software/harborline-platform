import assert from 'node:assert/strict'
import {mkdtempSync, readFileSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve} from 'node:path'
import test, {mock} from 'node:test'

import {recordPhase4Gate} from '../gate-contract.mjs'
import {createReusableStepRunner} from '../run-phase-4-gate.mjs'
import {decideStepReuse} from '../gate-step-evidence.mjs'

test('flagless PASS recording rewrites a galleryScenarios count staled by one', () => {
  const root = mkdtempSync(resolve(tmpdir(), 'phase4-evidence-'))
  try {
    const evidence = resolve(root, 'gate.json')
    writeFileSync(evidence, '{"status":"PASS","counts":{"galleryScenarios":465}}\n')
    const report = {status: 'PASS', counts: {galleryScenarios: 464}}
    assert.equal(recordPhase4Gate(evidence, report), true)
    assert.deepEqual(JSON.parse(readFileSync(evidence, 'utf8')), report)
  } finally {
    rmSync(root, {recursive: true, force: true})
  }
})

test('a failed gate never overwrites prior passing evidence', () => {
  const root = mkdtempSync(resolve(tmpdir(), 'phase4-evidence-'))
  try {
    const evidence = resolve(root, 'gate.json')
    const prior = '{"status":"PASS"}\n'
    writeFileSync(evidence, prior)
    assert.equal(recordPhase4Gate(evidence, {status: 'FAIL'}), false)
    assert.equal(readFileSync(evidence, 'utf8'), prior)
  } finally {
    rmSync(root, {recursive: true, force: true})
  }
})

test('a headless passing gate records no gallery assertion and never overwrites a prior full receipt', () => {
  const root = mkdtempSync(resolve(tmpdir(), 'phase4-evidence-'))
  try {
    const evidence = resolve(root, 'gate.json')
    const prior = '{"status":"PASS","results":[{"id":"gallery-gate","passed":true}]}\n'
    const headlessReport = {status: 'PASS', results: []}
    writeFileSync(evidence, prior)
    assert.equal(headlessReport.results.find(result => result.id === 'gallery-gate'), undefined)
    assert.equal(recordPhase4Gate(evidence, headlessReport, {headless: true}), false)
    assert.equal(readFileSync(evidence, 'utf8'), prior)
  } finally {
    rmSync(root, {recursive: true, force: true})
  }
})

// Oracle: ADR-0139 requires this invocation's results; ordinary runs retain the established
// matching-input reuse policy. Distinct literal reports make selecting the wrong path observable.
function reusableRunner(env, inputHash = 'matching-inputs') {
  const results = []
  const reports = new Map()
  const previousPass = {gate: {schemaVersion: 3, phase: 4, status: 'PASS', subject: {testedTree: 'prior-tree'},
    results: [{id: 'native-tests', passed: true, exitCode: 0, durationMs: 123,
      inputHash: 'matching-inputs', report: {counts: {total: 7}}}]}}
  const run = mock.fn((id, ...args) => {
    const report = {counts: {total: 5}}
    results.push({id, passed: true, report})
    reports.set(id, report)
    return report
  })
  const hashInputs = mock.fn(() => inputHash)
  // Call the real reuse policy so ordinary reuse is verified, not just selection of a stub.
  const decideReuse = mock.fn(decideStepReuse)
  const runReusable = createReusableStepRunner({env, repositoryRoot: '/fixture-repository',
    testedTree: 'current-tree', previousPass, results, reports, run, hashInputs, decideReuse})
  return {runReusable, run, hashInputs, decideReuse, results, reports, previousPass}
}

test('receipt orchestration runs reusable steps afresh and never consults reuse policy', () => {
  const f = reusableRunner({HARBORLINE_RECEIPT_RUN: '1'})
  const report = f.runReusable('native-tests', 'fixture-node', ['tests'], '/fixture-cwd', true, {FIXTURE: '1'})
  assert.deepEqual(report, {counts: {total: 5}})
  assert.equal(f.run.mock.callCount(), 1)
  assert.deepEqual(f.run.mock.calls[0].arguments,
    ['native-tests', 'fixture-node', ['tests'], '/fixture-cwd', true, {FIXTURE: '1'}])
  assert.equal(f.decideReuse.mock.callCount(), 0)
  assert.equal(f.hashInputs.mock.callCount(), 0)
  assert.equal(f.results.length, 1)
  assert.equal(f.results[0].reusedFrom, undefined)
  assert.deepEqual(f.reports.get('native-tests'), {counts: {total: 5}})
})

test('ordinary orchestration reuses matching input evidence without running the step', () => {
  const f = reusableRunner({})
  assert.deepEqual(f.runReusable('native-tests', 'fixture-node', ['tests']), {counts: {total: 7}})
  assert.equal(f.run.mock.callCount(), 0)
  assert.equal(f.hashInputs.mock.callCount(), 1)
  assert.deepEqual(f.hashInputs.mock.calls[0].arguments,
    [{repositoryRoot: '/fixture-repository', testedTree: 'current-tree', stepId: 'native-tests'}])
  assert.equal(f.decideReuse.mock.callCount(), 1)
  assert.deepEqual(f.decideReuse.mock.calls[0].arguments,
    [{stepId: 'native-tests', inputHash: 'matching-inputs', previousPass: f.previousPass}])
  assert.equal(f.results.length, 1)
  assert.equal(f.results[0].durationMs, 0)
  assert.deepEqual(f.results[0].reusedFrom, {
    evidence: 'docs/evidence/phase-4/gate.json', testedTree: 'prior-tree', inputHash: 'matching-inputs',
  })
  assert.deepEqual(f.reports.get('native-tests'), {counts: {total: 7}})
})

test('ordinary orchestration still runs a step when the reuse policy refuses changed inputs', () => {
  const f = reusableRunner({}, 'changed-inputs')
  assert.deepEqual(f.runReusable('native-tests', 'fixture-node', ['tests']), {counts: {total: 5}})
  assert.equal(f.decideReuse.mock.callCount(), 1)
  assert.equal(f.run.mock.callCount(), 1)
  assert.equal(f.results[0].inputHash, 'changed-inputs')
  assert.equal(f.results[0].reuseRefusal, 'declared input hash changed')
  assert.equal(f.results[0].reusedFrom, undefined)
})
