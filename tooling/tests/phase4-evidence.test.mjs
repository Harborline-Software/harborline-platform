import assert from 'node:assert/strict'
import {mkdtempSync, readFileSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve} from 'node:path'
import test from 'node:test'

import {recordPhase4Gate} from '../gate-contract.mjs'

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

// Oracle: a candidate receipt is evidence of this invocation, not a cached earlier PASS.
test('receipt orchestration executes reusable steps afresh without changing ordinary reuse', () => {
  const runner = readFileSync(new URL('../run-phase-4-gate.mjs', import.meta.url), 'utf8')
  const reusable = runner.split('function runReusable(')[1].split('// The app-shell fixture')[0]
  assert.match(reusable, /if \(process\.env\.HARBORLINE_RECEIPT_RUN === '1'\) return run\(id, executable, args, cwd, json, stepEnvironment\)/)
  assert.ok(reusable.indexOf('HARBORLINE_RECEIPT_RUN') < reusable.indexOf('decideStepReuse'))
  assert.match(reusable, /decideStepReuse\(/)
})
