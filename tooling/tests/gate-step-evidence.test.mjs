import assert from 'node:assert/strict'
import {execFileSync} from 'node:child_process'
import {mkdtempSync, rmSync, writeFileSync, mkdirSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve} from 'node:path'
import test from 'node:test'

import {decideStepReuse, hashStepInputs, loadPreviousPassEvidence} from '../gate-step-evidence.mjs'

function commitTree(repositoryRoot, content) {
  const target = resolve(repositoryRoot, 'specs/modules/ui/hlp.ui.button/interface.yaml')
  mkdirSync(resolve(target, '..'), {recursive: true})
  writeFileSync(target, content)
  execFileSync('git', ['add', '.'], {cwd: repositoryRoot})
  execFileSync('git', ['commit', '-m', 'fixture'], {cwd: repositoryRoot})
  return execFileSync('git', ['rev-parse', 'HEAD^{tree}'], {cwd: repositoryRoot, encoding: 'utf8'}).trim()
}

function passEvidence(inputHash) {
  return {
    gate: {
      schemaVersion: 3,
      phase: 4,
      status: 'PASS',
      subject: {testedTree: 'parent-tree'},
      results: [{
        id: 'ui-shared-conformance',
        passed: true,
        exitCode: 0,
        inputHash,
        report: {counts: {passedResults: 2}},
      }],
    },
  }
}

test('a UI-touching tree change forces ui-shared-conformance to run', () => {
  const repositoryRoot = mkdtempSync(resolve(tmpdir(), 'harborline-gate-step-evidence-'))
  try {
    execFileSync('git', ['init', '--quiet'], {cwd: repositoryRoot})
    execFileSync('git', ['config', 'user.email', 'gate-test@harborline.invalid'], {cwd: repositoryRoot})
    execFileSync('git', ['config', 'user.name', 'Gate Test'], {cwd: repositoryRoot})
    const treeA = commitTree(repositoryRoot, 'name: button\n')
    const hashA = hashStepInputs({repositoryRoot, testedTree: treeA, stepId: 'ui-shared-conformance'})
    const treeB = commitTree(repositoryRoot, 'name: button\n#')
    const hashB = hashStepInputs({repositoryRoot, testedTree: treeB, stepId: 'ui-shared-conformance'})

    assert.notEqual(hashB, hashA)
    assert.deepEqual(
      decideStepReuse({stepId: 'ui-shared-conformance', inputHash: hashB, previousPass: passEvidence(hashA)}),
      {mode: 'run-step', reason: 'declared input hash changed'},
    )
  } finally {
    rmSync(repositoryRoot, {recursive: true, force: true})
  }
})

test('only an exact complete prior PASS is reusable', () => {
  const inputHash = 'a'.repeat(64)
  const exact = decideStepReuse({stepId: 'ui-shared-conformance', inputHash, previousPass: passEvidence(inputHash)})
  assert.equal(exact.mode, 'reuse')
  assert.deepEqual(exact.previous.report, {counts: {passedResults: 2}})

  const cases = [
    [undefined, 'recorded gate evidence is unavailable'],
    [{reason: 'malformed'}, 'malformed'],
    [{gate: {...passEvidence(inputHash).gate, status: 'FAIL'}}, 'recorded gate evidence is not a schema-v3 phase-4 PASS'],
    [{gate: {...passEvidence(inputHash).gate, results: [{id: 'ui-shared-conformance', passed: true, exitCode: 0, inputHash}]}}, 'previous step evidence is not a complete PASS'],
    [{gate: {...passEvidence(inputHash).gate, results: [{id: 'ui-shared-conformance', passed: true, exitCode: 0, report: {}}]}}, 'previous step inputHash is absent'],
    [{gate: {...passEvidence(inputHash).gate, subject: {}}}, 'previous gate testedTree is absent'],
  ]
  for (const [previousPass, reason] of cases) {
    assert.deepEqual(
      decideStepReuse({stepId: 'ui-shared-conformance', inputHash, previousPass}),
      {mode: 'run-step', reason},
    )
  }
})

test('missing, malformed, and non-PASS gate files refuse reuse', () => {
  const directory = mkdtempSync(resolve(tmpdir(), 'harborline-gate-evidence-file-'))
  try {
    assert.match(loadPreviousPassEvidence(resolve(directory, 'missing.json')).reason, /absent or unreadable/)
    const evidencePath = resolve(directory, 'gate.json')
    writeFileSync(evidencePath, '{')
    assert.match(loadPreviousPassEvidence(evidencePath).reason, /absent or unreadable/)
    writeFileSync(evidencePath, JSON.stringify({schemaVersion: 3, phase: 4, status: 'FAIL'}))
    assert.equal(loadPreviousPassEvidence(evidencePath).reason, 'recorded gate evidence is not a schema-v3 phase-4 PASS')
  } finally {
    rmSync(directory, {recursive: true, force: true})
  }
})

test('unknown steps, invalid trees, and empty matches fail closed', () => {
  assert.throws(
    // package-consumers is structurally unreusable -- it produces the packed artifacts gallery-gate
    // installs, and the receipt's worktree is fresh -- so it is a stable example of an undeclared step.
    () => hashStepInputs({repositoryRoot: '.', testedTree: 'HEAD', stepId: 'package-consumers'}),
    /has no declared inputs/,
  )
  const repositoryRoot = mkdtempSync(resolve(tmpdir(), 'harborline-gate-empty-tree-'))
  try {
    execFileSync('git', ['init', '--quiet'], {cwd: repositoryRoot})
    assert.throws(
      () => hashStepInputs({repositoryRoot, testedTree: 'not-a-tree', stepId: 'ui-shared-conformance'}),
      /Command failed/,
    )
  } finally {
    rmSync(repositoryRoot, {recursive: true, force: true})
  }
})
