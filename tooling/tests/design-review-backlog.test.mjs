import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import {readFileSync} from 'node:fs'
import {resolve} from 'node:path'
import test from 'node:test'
import {evaluateStepStdout} from '../gate-step-evidence.mjs'
import {designReviewDecision} from '../gates/design-review-status.mjs'
import {reviewVerdict} from '../gates/design-review.mjs'

const root = resolve(import.meta.dirname, '../..')
const moduleId = 'hlp.ui.backlog-fixture'
const backlog = {ticket: 334, deadline: '2026-09-30', commit: '72b32de7fa0bf6abf7569ad621fde00a9b8c03fc', modules: [moduleId]}
function moduleWithReview(expired = true) {
  const record = {schemaVersion: 1, reviewer: 'A Person', recordedAt: '2026-08-26',
    verdict: 'approved', reference: {surface: {'Component.razor': 'before'}}}
  const [status, note] = reviewVerdict({record, revision: 'revision',
    surface: {'Component.razor': expired ? 'after' : 'before'}})
  return {moduleId, catalogStatus: 'extracted-candidate', terminal: false,
    gates: [{id: 'assertDesignReview', status, note}]}
}
function evaluate(modules, date, list = backlog) {
  const options = {backlog: list, now: new Date(date)}
  const decided = modules.map(module => ({...module, gates: module.gates.map(row => {
    if (row.id !== 'assertDesignReview') return row
    const {status, note} = designReviewDecision(module.moduleId, row, options)
    return {...row, status, note}
  })}))
  return evaluateStepStdout({stepId: 'ui-gate-model', json: true, status: 0,
    stdout: JSON.stringify({modules: decided}), designReviewOptions: options})
}

test('the backlog rule is identical for module ids reached through POSIX and Windows paths', () => {
  const expired = {status: 'FAIL', note: 'verdict EXPIRED: approved surface changed'}
  const options = {backlog, now: new Date('2026-09-08T12:00:00.000Z')}
  for (const path of [
    '/checkout/conformance/hlp.ui.backlog-fixture/fixtures.yaml',
    'C:\\checkout\\conformance\\hlp.ui.backlog-fixture\\fixtures.yaml',
  ]) {
    assert.deepEqual(designReviewDecision(path, expired, options), {
      status: 'PASS',
      note: 'EXPIRED (backlog 334 until 2026-09-30)',
      expired: true,
      backlogged: true,
      moduleId,
    })
  }
  for (const path of [
    '/checkout/conformance/hlp.ui.not-backlogged/fixtures.yaml',
    'C:\\checkout\\conformance\\hlp.ui.not-backlogged\\fixtures.yaml',
  ]) {
    assert.equal(designReviewDecision(path, expired, options).status, 'FAIL')
  }
})

test('a listed expired module is a passing note before the deadline and fails on and after it', () => {
  const module = moduleWithReview()
  assert.equal(module.gates[0].status, 'FAIL')
  assert.match(module.gates[0].note, /EXPIRED/)
  const before = evaluate([module], '2026-09-29T23:59:59.999Z')
  assert.equal(before.status, 0)
  assert.equal(before.report.status, 'PASS')
  assert.equal(before.report.modules[0].gates[0].status, 'PASS')
  assert.equal(before.report.modules[0].gates[0].note, 'EXPIRED (backlog 334 until 2026-09-30)')
  assert.deepEqual(before.report.designReview.expired, [moduleId])
  assert.equal(before.report.designReview.backlog.count, 1)
  assert.equal(before.report.designReview.backlog.deadline, '2026-09-30')
  for (const date of ['2026-09-30T00:00:00.000Z', '2026-10-01T00:00:00.000Z']) {
    const after = evaluate([module], date)
    assert.equal(after.status, 1)
    assert.equal(after.report.status, 'FAIL')
    assert.match(after.failure, /hlp\.ui\.backlog-fixture/)
    assert.deepEqual(after.report.modules, [module])
  }
})

test('an unlisted expired module fails before the deadline', () => {
  const result = evaluate([moduleWithReview()], '2026-09-08', {...backlog, modules: []})
  assert.equal(result.status, 1)
  assert.equal(result.report.status, 'FAIL')
  assert.match(result.failure, /hlp\.ui\.backlog-fixture/)
})

test('a listed non-expired module fails with its name until removed from the backlog', () => {
  const module = moduleWithReview(false)
  assert.equal(module.gates[0].status, 'PASS')
  for (const date of ['2026-09-08', '2026-10-01']) {
    const result = evaluate([module], date)
    assert.equal(result.status, 1)
    assert.equal(result.report.status, 'FAIL')
    assert.match(result.failure, /backlog.*not expired.*hlp\.ui\.backlog-fixture/i)
    assert.equal(evaluate([module], date, {...backlog, modules: []}).status, 0)
  }
})

test('the checked-in backlog equals the real sweep expired set today', () => {
  const list = JSON.parse(readFileSync(resolve(root, 'docs/evidence/design-review/expired-backlog.json'), 'utf8'))
  assert.equal(list.ticket, 334)
  assert.equal(list.deadline, '2026-09-30')
  assert.match(list.commit, /^[0-9a-f]{40}$/)
  const sweep = spawnSync(process.execPath, ['tooling/gates/run-ui-gate-model.mjs', '--json'],
    {cwd: root, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024})
  const report = JSON.parse(sweep.stdout)
  const expired = [...report.designReview.expired].sort()
  assert.deepEqual([...list.modules].sort(), expired)
  assert.equal(new Set(list.modules).size, list.modules.length)
  const expected = expired.length && new Date() >= new Date(`${list.deadline}T00:00:00.000Z`) ? 'FAIL' : 'PASS'
  assert.equal(report.status, expected)
  assert.equal(sweep.status, expected === 'FAIL' ? 1 : 0, sweep.stderr)
  assert.equal(report.designReview.backlog.count, list.modules.length)
  assert.equal(report.designReview.backlog.deadline, list.deadline)
  const backlogRows = report.modules.filter(module => list.modules.includes(module.moduleId))
    .map(module => module.gates.find(row => row.id === 'assertDesignReview'))
  assert.equal(backlogRows.length, 51)
  assert.ok(backlogRows.every(row => row.status === 'PASS'
    && row.note === 'EXPIRED (backlog 334 until 2026-09-30)'))
  assert.ok(report.modules.filter(module => list.modules.includes(module.moduleId))
    .map(module => module.gates.find(row => row.id === 'assertDesignQuality'))
    .every(row => row.status !== 'FAIL' || !/inherited from assertDesignReview/.test(row.note)))
  const aggregated = evaluateStepStdout({stepId: 'ui-gate-model', json: true, status: sweep.status, stdout: sweep.stdout})
  assert.equal(aggregated.report.status, expected)
  assert.equal(aggregated.status, sweep.status)
})
