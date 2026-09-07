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
