import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import {readFileSync} from 'node:fs'
import {resolve} from 'node:path'
import test from 'node:test'
import {evaluateStepStdout} from '../gate-step-evidence.mjs'
import {
  FROZEN_BACKLOG_DIGEST, backlogDigest, designReviewDecision,
} from '../gates/design-review-status.mjs'
import {reviewVerdict} from '../gates/design-review.mjs'

const root = resolve(import.meta.dirname, '../..')
const moduleId = 'hlp.ui.backlog-fixture'
const backlog = {ticket: 334, commit: '72b32de7fa0bf6abf7569ad621fde00a9b8c03fc', modules: [moduleId]}
// The fixture backlog is a different list from the real one, so it carries its own freeze. Tests
// that are not about the freeze must not trip it, and the one that is says so by name.
const frozen = backlogDigest(backlog.modules)
const AMNESTIED = 'EXPIRED (backlog 334; owed before R1 is released publicly)'

function moduleWithReview(expired = true, id = moduleId) {
  const record = {schemaVersion: 1, reviewer: 'A Person', recordedAt: '2026-08-26',
    verdict: 'approved', reference: {surface: {'Component.razor': 'before'}}}
  const [status, note] = reviewVerdict({record, revision: 'revision',
    surface: {'Component.razor': expired ? 'after' : 'before'}})
  return {moduleId: id, catalogStatus: 'extracted-candidate', terminal: false,
    gates: [{id: 'assertDesignReview', status, note}]}
}
function evaluate(modules, list = backlog, digest = backlogDigest(list?.modules ?? [])) {
  const options = {backlog: list, frozen: digest}
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
  const options = {backlog}
  for (const path of [
    '/checkout/conformance/hlp.ui.backlog-fixture/fixtures.yaml',
    'C:\\checkout\\conformance\\hlp.ui.backlog-fixture\\fixtures.yaml',
  ]) {
    assert.deepEqual(designReviewDecision(path, expired, options), {
      status: 'PASS',
      note: AMNESTIED,
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

// T-631. The amnesty used to lapse at 00:00 UTC on 2026-09-30. It no longer ends, and no check
// reads a clock: the same tree is judged the same way whatever the date is.
test('a listed expired module is a passing note, and no date changes that', () => {
  const module = moduleWithReview()
  assert.equal(module.gates[0].status, 'FAIL')
  assert.match(module.gates[0].note, /EXPIRED/)
  const result = evaluate([module])
  assert.equal(result.status, 0)
  assert.equal(result.report.status, 'PASS')
  assert.equal(result.report.modules[0].gates[0].status, 'PASS')
  assert.equal(result.report.modules[0].gates[0].note, AMNESTIED)
  assert.deepEqual(result.report.designReview.expired, [moduleId])
  assert.equal(result.report.designReview.backlog.count, 1)
  assert.equal(result.report.designReview.unfrozenBacklog, false)
  assert.ok(!('deadline' in result.report.designReview.backlog))
  assert.ok(!/deadline|2026-09-30/.test(JSON.stringify(result.report.designReview)))
})

test('an unlisted expired module fails', () => {
  const result = evaluate([moduleWithReview()], {...backlog, modules: []})
  assert.equal(result.status, 1)
  assert.equal(result.report.status, 'FAIL')
  assert.match(result.failure, /hlp\.ui\.backlog-fixture/)
})

test('a listed non-expired module fails with its name until removed from the backlog', () => {
  const module = moduleWithReview(false)
  assert.equal(module.gates[0].status, 'PASS')
  const result = evaluate([module])
  assert.equal(result.status, 1)
  assert.equal(result.report.status, 'FAIL')
  assert.match(result.failure, /backlog.*not expired.*hlp\.ui\.backlog-fixture/i)
  assert.equal(evaluate([module], {...backlog, modules: []}).status, 0)
})

// T-631's third part. Without this the ruling decays into "expiry is advisory", one appended line
// at a time: every newly expired module could simply join the amnesty.
test('a module whose review expires after the ruling cannot be amnestied by appending it', () => {
  const late = 'hlp.ui.expired-after-the-ruling'
  const grown = {...backlog, modules: [...backlog.modules, late]}
  const modules = [moduleWithReview(), moduleWithReview(true, late)]

  // Unlisted, it fails on its own: the freeze is not what makes a new expiry fail.
  const unlisted = evaluate(modules)
  assert.equal(unlisted.status, 1)
  assert.match(unlisted.failure, new RegExp(late))

  // Listed, the digest no longer matches the frozen one and the whole backlog is refused, so
  // appending buys nothing. `frozen` stays the ORIGINAL list's digest, which is the freeze.
  const extended = evaluate(modules, grown, frozen)
  assert.equal(extended.status, 1)
  assert.equal(extended.report.designReview.unfrozenBacklog, true)
  assert.match(extended.failure, /FROZEN/)
  assert.match(extended.failure, /FROZEN_BACKLOG_DIGEST/)
})

test('the checked-in backlog carries no date and matches the frozen digest', () => {
  const list = JSON.parse(readFileSync(resolve(root, 'docs/evidence/design-review/expired-backlog.json'), 'utf8'))
  assert.equal(list.ticket, 334)
  assert.match(list.commit, /^[0-9a-f]{40}$/)
  assert.ok(!('deadline' in list), 'T-631: the amnesty is not derived from a date any more')
  assert.equal(new Set(list.modules).size, list.modules.length)
  assert.equal(list.modules.length, 50)
  assert.ok(!list.modules.includes('hlp.ui.select-field'), 'SelectField has Chris\'s renewed approval')
  assert.equal(backlogDigest(list.modules), FROZEN_BACKLOG_DIGEST)
})

test('the checked-in backlog equals the real sweep expired set, and the sweep passes', () => {
  const list = JSON.parse(readFileSync(resolve(root, 'docs/evidence/design-review/expired-backlog.json'), 'utf8'))
  const sweep = spawnSync(process.execPath, ['tooling/gates/run-ui-gate-model.mjs', '--json'],
    {cwd: root, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024})
  const report = JSON.parse(sweep.stdout)
  const expired = [...report.designReview.expired].sort()
  assert.deepEqual([...list.modules].sort(), expired)
  // No `expected` derived from a clock: the amnesty holds because the backlog holds.
  assert.equal(report.status, 'PASS')
  assert.equal(sweep.status, 0, sweep.stderr)
  assert.equal(report.designReview.backlog.count, list.modules.length)
  assert.equal(report.designReview.backlog.frozen, FROZEN_BACKLOG_DIGEST)
  assert.equal(report.designReview.unfrozenBacklog, false)
  const backlogRows = report.modules.filter(module => list.modules.includes(module.moduleId))
    .map(module => module.gates.find(row => row.id === 'assertDesignReview'))
  // Chris's 2026-09-20 SelectField review removes one expired entry.
  assert.equal(backlogRows.length, 50)
  assert.ok(backlogRows.every(row => row.status === 'PASS' && row.note === AMNESTIED))
  assert.ok(report.modules.filter(module => list.modules.includes(module.moduleId))
    .map(module => module.gates.find(row => row.id === 'assertDesignQuality'))
    .every(row => row.status !== 'FAIL' || !/inherited from assertDesignReview/.test(row.note)))
  const aggregated = evaluateStepStdout({stepId: 'ui-gate-model', json: true, status: sweep.status, stdout: sweep.stdout})
  assert.equal(aggregated.report.status, 'PASS')
  assert.equal(aggregated.status, sweep.status)
})

// The acceptance line the ruling asked to be proved by running rather than by argument. The shim
// is the harness T-631's earlier lane left behind: it moves the clock past the withdrawn deadline
// before anything else loads, so a surviving `new Date()` comparison would still see 2026-10-01.
test('the sweep exits zero with the remaining 50 expired when the clock is past the withdrawn deadline', () => {
  const sweep = spawnSync(process.execPath,
    ['--import', './tooling/tests/fixtures/clock-2026-10-01.mjs', 'tooling/gates/run-ui-gate-model.mjs', '--json'],
    {cwd: root, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024})
  assert.equal(sweep.status, 0, sweep.stderr)
  const report = JSON.parse(sweep.stdout)
  assert.equal(report.status, 'PASS')
  assert.equal(report.designReview.expired.length, 50)
  assert.deepEqual(report.designReview.failingExpired, [])
})
