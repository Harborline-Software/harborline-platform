import assert from 'node:assert/strict'
import {execFileSync, spawnSync} from 'node:child_process'
import {existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync, mkdirSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve} from 'node:path'
import test from 'node:test'

import {decideStepReuse, hashStepInputs, loadPreviousPassEvidence} from '../gate-step-evidence.mjs'

function commitTree(repositoryRoot, content, targetPath = 'specs/modules/ui/hlp.ui.button/interface.yaml') {
  const target = resolve(repositoryRoot, targetPath)
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
        report: {counts: {passedResults: 2}, schedule: {mode: 'original'}},
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
      decideStepReuse({stepId: 'ui-shared-conformance', inputHash: hashB, previousPass: passEvidence(hashA), sharedSchedule: 'original', repositoryRoot}),
      {mode: 'run-step', reason: 'declared input hash changed'},
    )
  } finally {
    rmSync(repositoryRoot, {recursive: true, force: true})
  }
})

test('changing a released detail definition invalidates native and shared rendering evidence', () => {
  const repositoryRoot = mkdtempSync(resolve(tmpdir(), 'harborline-gate-pack-evidence-'))
  try {
    execFileSync('git', ['init', '--quiet'], {cwd: repositoryRoot})
    execFileSync('git', ['config', 'user.email', 'gate-test@harborline.invalid'], {cwd: repositoryRoot})
    execFileSync('git', ['config', 'user.name', 'Gate Test'], {cwd: repositoryRoot})
    const target = '_shared/packs/platform/platform-pack.export.json'
    const treeA = commitTree(repositoryRoot, '{"detail":"generation-v1"}\n', target)
    const treeB = commitTree(repositoryRoot, '{"detail":"generation-v2"}\n', target)
    for (const stepId of ['native-tests', 'ui-shared-conformance']) {
      const hashA = hashStepInputs({repositoryRoot, testedTree: treeA, stepId})
      const hashB = hashStepInputs({repositoryRoot, testedTree: treeB, stepId})
      assert.notEqual(hashB, hashA, `${stepId} must observe the released definition change`)
      const previousPass = passEvidence(hashA)
      previousPass.gate.results[0].id = stepId
      assert.deepEqual(decideStepReuse({stepId, inputHash: hashB, previousPass, sharedSchedule: 'original', repositoryRoot}),
        {mode: 'run-step', reason: 'declared input hash changed'})
    }
  } finally {
    rmSync(repositoryRoot, {recursive: true, force: true})
  }
})

test('only an exact complete prior PASS is reusable', () => {
  const repositoryRoot = mkdtempSync(resolve(tmpdir(), 'harborline-gate-exact-reuse-'))
  try {
    const inputHash = 'a'.repeat(64)
    const exact = decideStepReuse({stepId: 'ui-shared-conformance', inputHash, previousPass: passEvidence(inputHash), sharedSchedule: 'original', repositoryRoot})
    assert.equal(exact.mode, 'reuse')
    assert.deepEqual(exact.previous.report, {counts: {passedResults: 2}, schedule: {mode: 'original'}})

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
        decideStepReuse({stepId: 'ui-shared-conformance', inputHash, previousPass, sharedSchedule: 'original', repositoryRoot}),
        {mode: 'run-step', reason},
      )
    }
  } finally {
    rmSync(repositoryRoot, {recursive: true, force: true})
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

test('changing the actual shared scheduler helper invalidates only its declared reusable step', () => {
  const repositoryRoot = mkdtempSync(resolve(tmpdir(), 'harborline-shared-helper-evidence-'))
  try {
    execFileSync('git', ['init', '--quiet'], {cwd: repositoryRoot})
    execFileSync('git', ['config', 'user.email', 'gate-test@harborline.invalid'], {cwd: repositoryRoot})
    execFileSync('git', ['config', 'user.name', 'Gate Test'], {cwd: repositoryRoot})
    commitTree(repositoryRoot, 'name: button\n')
    const treeA = commitTree(repositoryRoot, 'export const fixture = "first"\n', 'tooling/shared-module-scheduler.mjs')
    const treeB = commitTree(repositoryRoot, 'export const fixture = "second"\n', 'tooling/shared-module-scheduler.mjs')
    const sharedA = hashStepInputs({repositoryRoot, testedTree: treeA, stepId: 'ui-shared-conformance'})
    const sharedB = hashStepInputs({repositoryRoot, testedTree: treeB, stepId: 'ui-shared-conformance'})
    assert.notEqual(sharedB, sharedA)
    assert.equal(hashStepInputs({repositoryRoot, testedTree: treeA, stepId: 'native-tests'}),
      hashStepInputs({repositoryRoot, testedTree: treeB, stepId: 'native-tests'}))
    assert.deepEqual(decideStepReuse({stepId: 'ui-shared-conformance', inputHash: sharedB,
      previousPass: passEvidence(sharedA), sharedSchedule: 'original', repositoryRoot}),
    {mode: 'run-step', reason: 'declared input hash changed'})
  } finally {
    rmSync(repositoryRoot, {recursive: true, force: true})
  }
})

test('non-original schedules cannot reuse a UI PASS while unrelated steps retain their exact evidence', () => {
  const repositoryRoot = mkdtempSync(resolve(tmpdir(), 'harborline-gate-current-schedule-'))
  try {
    const inputHash = 'b'.repeat(64)
    for (const sharedSchedule of ['exclusive-tail', '', 'unknown']) {
      assert.deepEqual(decideStepReuse({stepId: 'ui-shared-conformance', inputHash,
        previousPass: passEvidence(inputHash), sharedSchedule, repositoryRoot}),
      {mode: 'run-step', reason: 'ui-shared-conformance reuse requires original scheduling'})
      for (const stepId of ['native-tests', 'gallery-gate']) {
        const previousPass = passEvidence(inputHash)
        previousPass.gate.results[0].id = stepId
        assert.equal(decideStepReuse({stepId, inputHash, previousPass, sharedSchedule, repositoryRoot}).mode, 'reuse')
      }
    }
    assert.equal(decideStepReuse({stepId: 'ui-shared-conformance', inputHash,
      previousPass: passEvidence(inputHash), sharedSchedule: 'original', repositoryRoot}).mode, 'reuse')
  } finally {
    rmSync(repositoryRoot, {recursive: true, force: true})
  }
})

test('the real default reuse boundary reads each isolated child schedule environment', () => {
  const repositoryRoot = mkdtempSync(resolve(tmpdir(), 'harborline-gate-child-schedule-'))
  const moduleUrl = new URL('../gate-step-evidence.mjs', import.meta.url).href
  const source = `
    import {decideStepReuse} from ${JSON.stringify(moduleUrl)}
    const inputHash = 'c'.repeat(64)
    const previousPass = {gate: {schemaVersion: 3, phase: 4, status: 'PASS',
      subject: {testedTree: 'literal-tree'}, results: [{id: 'ui-shared-conformance',
        passed: true, exitCode: 0, inputHash, report: {status: 'PASS', schedule: {mode: 'original'}}}]}}
    process.stdout.write(JSON.stringify(decideStepReuse({stepId: 'ui-shared-conformance', inputHash, previousPass,
      repositoryRoot: ${JSON.stringify(repositoryRoot)}})))
  `
  try {
    for (const [selector, mode] of [[undefined, 'reuse'], ['original', 'reuse'],
      ['exclusive-tail', 'run-step'], ['', 'run-step'], ['unknown', 'run-step']]) {
      const env = {...process.env}
      if (selector === undefined) delete env.HARBORLINE_SHARED_SCHEDULE
      else env.HARBORLINE_SHARED_SCHEDULE = selector
      const child = spawnSync(process.execPath, ['--input-type=module', '--eval', source], {env, encoding: 'utf8'})
      assert.equal(child.status, 0, child.stderr)
      const outcome = JSON.parse(child.stdout)
      assert.equal(outcome.mode, mode, `selector ${String(selector)}`)
      if (mode === 'run-step') assert.equal(outcome.reason, 'ui-shared-conformance reuse requires original scheduling')
    }
  } finally {
    rmSync(repositoryRoot, {recursive: true, force: true})
  }
})

test('an original UI run refuses every non-original or unrecorded previous schedule', () => {
  const repositoryRoot = mkdtempSync(resolve(tmpdir(), 'harborline-gate-previous-schedule-'))
  try {
    const artifact = resolve(repositoryRoot, '.claude/gate-evidence/ui-shared-schedule.json')
    mkdirSync(resolve(artifact, '..'), {recursive: true})
    writeFileSync(artifact, 'literal old schedule evidence\n')
    const inputHash = 'd'.repeat(64)
    for (const schedule of [undefined, null, {}, {mode: null}, {mode: ''}, {mode: 'exclusive-tail'}, {mode: 'unknown'}]) {
      const previousPass = passEvidence(inputHash)
      if (schedule === undefined) delete previousPass.gate.results[0].report.schedule
      else previousPass.gate.results[0].report.schedule = schedule
      const before = JSON.stringify(previousPass)
      assert.deepEqual(decideStepReuse({stepId: 'ui-shared-conformance', inputHash,
        previousPass, sharedSchedule: 'original', repositoryRoot}),
      {mode: 'run-step', reason: 'previous ui-shared-conformance scheduling is not original'})
      assert.equal(readFileSync(artifact, 'utf8'), 'literal old schedule evidence\n')
      assert.equal(JSON.stringify(previousPass), before)
    }
  } finally {
    rmSync(repositoryRoot, {recursive: true, force: true})
  }
})

test('a truncated UI report keeps the existing reason before checking its mode or removing stale evidence', () => {
  const repositoryRoot = mkdtempSync(resolve(tmpdir(), 'harborline-gate-truncated-schedule-'))
  try {
    const artifact = resolve(repositoryRoot, '.claude/gate-evidence/ui-shared-schedule.json')
    mkdirSync(resolve(artifact, '..'), {recursive: true})
    writeFileSync(artifact, 'literal stale evidence must remain on refusal\n')
    const inputHash = '7'.repeat(64)
    const previousPass = passEvidence(inputHash)
    // Independent literal of the existing bounded report format; all PASS/hash/tree/current-mode
    // conditions otherwise permit reuse, so the former mode-first guard gives the wrong reason.
    previousPass.gate.results[0].report = '{"status":"PASS"}\n[truncated after 16384 characters]'
    const before = JSON.stringify(previousPass)

    assert.deepEqual(decideStepReuse({stepId: 'ui-shared-conformance', inputHash,
      previousPass, sharedSchedule: 'original', repositoryRoot}),
    {mode: 'run-step', reason: 'previous step report was truncated'})

    assert.equal(readFileSync(artifact, 'utf8'), 'literal stale evidence must remain on refusal\n')
    assert.equal(JSON.stringify(previousPass), before)
  } finally {
    rmSync(repositoryRoot, {recursive: true, force: true})
  }
})

test('successful UI reuse removes only the exact stale schedule artifact and permits its absence', () => {
  const repositoryRoot = mkdtempSync(resolve(tmpdir(), 'harborline-gate-clean-schedule-'))
  try {
    const evidence = resolve(repositoryRoot, '.claude/gate-evidence')
    mkdirSync(evidence, {recursive: true})
    const artifact = resolve(evidence, 'ui-shared-schedule.json')
    writeFileSync(artifact, 'stale intervals from a different run\n')
    writeFileSync(resolve(evidence, 'report.json'), 'sibling report\n')
    writeFileSync(resolve(evidence, 'ui-shared-conformance.log'), 'sibling failure log\n')
    const inputHash = 'e'.repeat(64)
    const previousPass = passEvidence(inputHash)
    const before = JSON.stringify(previousPass)
    const outcome = decideStepReuse({stepId: 'ui-shared-conformance', inputHash,
      previousPass, sharedSchedule: 'original', repositoryRoot})
    assert.equal(outcome.mode, 'reuse')
    assert.equal(outcome.previous, previousPass.gate.results[0])
    assert.equal(existsSync(artifact), false)
    assert.equal(readFileSync(resolve(evidence, 'report.json'), 'utf8'), 'sibling report\n')
    assert.equal(readFileSync(resolve(evidence, 'ui-shared-conformance.log'), 'utf8'), 'sibling failure log\n')
    assert.equal(JSON.stringify(previousPass), before)
    assert.equal(decideStepReuse({stepId: 'ui-shared-conformance', inputHash,
      previousPass, sharedSchedule: 'original', repositoryRoot}).mode, 'reuse')
    assert.equal(existsSync(artifact), false)
  } finally {
    rmSync(repositoryRoot, {recursive: true, force: true})
  }
})

test('unrelated or refused reuse leaves the exact schedule artifact untouched', () => {
  const repositoryRoot = mkdtempSync(resolve(tmpdir(), 'harborline-gate-retain-schedule-'))
  try {
    const artifact = resolve(repositoryRoot, '.claude/gate-evidence/ui-shared-schedule.json')
    mkdirSync(resolve(artifact, '..'), {recursive: true})
    writeFileSync(artifact, 'unchanged schedule evidence\n')
    const inputHash = 'f'.repeat(64)
    for (const stepId of ['native-tests', 'gallery-gate']) {
      const previousPass = passEvidence(inputHash)
      previousPass.gate.results[0].id = stepId
      previousPass.gate.results[0].report.schedule.mode = 'exclusive-tail'
      assert.equal(decideStepReuse({stepId, inputHash, previousPass,
        sharedSchedule: 'exclusive-tail', repositoryRoot}).mode, 'reuse')
      assert.equal(readFileSync(artifact, 'utf8'), 'unchanged schedule evidence\n')
    }
    const previousPass = passEvidence(inputHash)
    assert.deepEqual(decideStepReuse({stepId: 'ui-shared-conformance', inputHash,
      previousPass, sharedSchedule: 'exclusive-tail', repositoryRoot}),
    {mode: 'run-step', reason: 'ui-shared-conformance reuse requires original scheduling'})
    assert.deepEqual(decideStepReuse({stepId: 'ui-shared-conformance', inputHash: 'different',
      previousPass, sharedSchedule: 'original', repositoryRoot}),
    {mode: 'run-step', reason: 'declared input hash changed'})
    assert.equal(readFileSync(artifact, 'utf8'), 'unchanged schedule evidence\n')
  } finally {
    rmSync(repositoryRoot, {recursive: true, force: true})
  }
})

test('UI artifact cleanup is nonrecursive and a failure cannot silently return reuse', () => {
  const repositoryRoot = mkdtempSync(resolve(tmpdir(), 'harborline-gate-cleanup-failure-'))
  try {
    const artifact = resolve(repositoryRoot, '.claude/gate-evidence/ui-shared-schedule.json')
    mkdirSync(artifact, {recursive: true})
    writeFileSync(resolve(artifact, 'keep.txt'), 'directory must not be recursively removed\n')
    const inputHash = '9'.repeat(64)
    assert.throws(() => decideStepReuse({stepId: 'ui-shared-conformance', inputHash,
      previousPass: passEvidence(inputHash), sharedSchedule: 'original', repositoryRoot}))
    assert.equal(readFileSync(resolve(artifact, 'keep.txt'), 'utf8'), 'directory must not be recursively removed\n')
  } finally {
    rmSync(repositoryRoot, {recursive: true, force: true})
  }
})
