import {test} from 'node:test'
import assert from 'node:assert/strict'
import {mkdtempSync, writeFileSync, rmSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {join} from 'node:path'
import {spawnSync} from 'node:child_process'
import {fileURLToPath} from 'node:url'
import {buildActionsTimingReport} from '../actions-timing-report.mjs'

// Literal UTC times form the oracle: 10 seconds before creation, 5 waiting,
// then 20 executing. No expected value is read from production output.
const run = {id: 1, head_sha: 'abc', run_attempt: 2, status: 'completed', conclusion: 'failure',
  created_at: '2026-10-01T00:00:00Z', run_started_at: '2026-10-01T00:00:02Z'}
const job = {id: 10, name: 'gate', run_id: 1, run_attempt: 2, head_sha: 'abc',
  status: 'completed', conclusion: 'failure', created_at: '2026-10-01T00:00:10Z',
  started_at: '2026-10-01T00:00:15Z', completed_at: '2026-10-01T00:00:35Z',
  steps: [{number: 1, name: 'test', status: 'completed', conclusion: 'failure',
    started_at: '2026-10-01T00:00:20Z', completed_at: '2026-10-01T00:00:30Z'}]}
const report = (jobs = [job], extra = {}) => buildActionsTimingReport({run, jobs, ...extra})

test('separates dispatch, dependency/orchestration delay, waiting and execution', () => {
  const result = report()
  assert.equal(result.run.dispatchWaitMs, 2000)
  assert.equal(result.run.observedWallMs, 35000)
  assert.equal(result.jobs[0].beforeJobCreatedMs, 10000)
  assert.equal(result.jobs[0].waitMs, 5000)
  assert.equal(result.jobs[0].executionMs, 20000)
  assert.equal(result.slowSteps[0].executionMs, 10000)
  assert.equal(result.run.headSha, 'abc')
  assert.equal(result.run.attempt, 2)
  assert.equal(result.run.previousAttemptCount, 1)
  assert.equal(result.run.conclusion, 'failure')
  assert.equal(result.slowSteps[0].conclusion, 'failure')
})

test('cancelled, skipped and incomplete jobs remain visible with unknown durations', () => {
  for (const conclusion of ['cancelled', 'skipped', null]) {
    const result = report([{...job, conclusion, started_at: null, completed_at: null}])
    assert.equal(result.jobs[0].conclusion, conclusion)
    assert.equal(result.jobs[0].executionMs, null)
    assert.equal(result.jobs[0].waitMs, null)
    assert.equal(result.run.observedWallMs, null)
  }
})

test('reversed and malformed timestamps are unknown; a true zero stays zero', () => {
  assert.equal(report([{...job, completed_at: job.created_at}]).jobs[0].executionMs, null)
  assert.equal(report([{...job, started_at: 'invalid'}]).jobs[0].executionMs, null)
  assert.equal(report([{...job, completed_at: job.started_at}]).jobs[0].executionMs, 0)
})

test('rejects mixed SHAs, runs, attempts and duplicate jobs', () => {
  for (const change of [{head_sha: 'other'}, {run_id: 2}, {run_attempt: 1}]) {
    assert.throws(() => report([{...job, ...change}]), /exact run, attempt and SHA/)
  }
  assert.throws(() => report([job, job]), /unique/)
})

test('never guesses a dependency graph from timestamp order', () => {
  assert.equal(report().criticalExecutionPath.available, false)
  assert.equal(report([]).criticalExecutionPath.available, false)
})

test('parallel branches are not summed in the critical execution path', () => {
  const parallel = {...job, id: 11, name: 'parallel', completed_at: '2026-10-01T00:00:25Z'}
  const collect = {...job, id: 12, name: 'collect', started_at: '2026-10-01T00:00:40Z', completed_at: '2026-10-01T00:00:45Z'}
  const result = report([job, parallel, collect], {dependencies: {gate: [], parallel: [], collect: ['gate', 'parallel']}})
  assert.deepEqual(result.criticalExecutionPath.jobIds, [10, 12])
  assert.equal(result.criticalExecutionPath.executionMs, 25000)
})

test('rejects incomplete, unknown and cyclic graphs; incomplete timing is unavailable', () => {
  for (const dependencies of [{}, {gate: ['missing']}, {gate: ['gate']}]) {
    assert.throws(() => report([job], {dependencies}))
  }
  assert.equal(report([{...job, started_at: null}], {dependencies: {gate: []}}).criticalExecutionPath.available, false)
})

test('retains successful gate timing, failures, nested durations and reuse provenance', () => {
  const result = report([job], {gateReports: [{jobId: 10, report: {status: 'PASS', subject: {testedTree: 'tree'},
    results: [{id: 'native', passed: true, durationMs: 500, report: {results: [{id: 'suite', passed: true, durationMs: 400}]}},
      {id: 'shared', passed: true, durationMs: 0, reusedFrom: {testedTree: 'previous'}},
      {id: 'failed', passed: false, durationMs: 600, exitCode: 1}]}}]})
  assert.equal(result.gateReports[0].status, 'PASS')
  assert.deepEqual(result.gateReports[0].subject, {testedTree: 'tree'})
  assert.deepEqual(result.gateReports[0].slowSteps.map(step => step.executionMs), [600, 500, 400, 0])
  assert.equal(result.gateReports[0].slowSteps[0].passed, false)
  assert.deepEqual(result.gateReports[0].slowSteps[3].reusedFrom, {testedTree: 'previous'})
  assert.throws(() => report([job], {gateReports: [{jobId: 99, report: {}}]}), /unknown job/)
})

test('CLI reads paginated exports, refuses incomplete pages and keeps failure outcome', () => {
  const scratch = mkdtempSync(join(tmpdir(), 'actions-timing-'))
  try {
    const runPath = join(scratch, 'run.json'), jobsPath = join(scratch, 'jobs.json')
    writeFileSync(runPath, JSON.stringify(run))
    const cli = () => spawnSync(process.execPath, [fileURLToPath(new URL('../actions-timing-report.mjs', import.meta.url)), runPath, jobsPath], {encoding: 'utf8'})
    writeFileSync(jobsPath, JSON.stringify([{total_count: 2, jobs: [job]}, {total_count: 2, jobs: [{...job, id: 11, name: 'other'}]}]))
    const result = cli()
    assert.equal(result.status, 0)
    assert.equal(JSON.parse(result.stdout).run.conclusion, 'failure')
    assert.equal(JSON.parse(result.stdout).jobs.length, 2)
    writeFileSync(jobsPath, JSON.stringify({total_count: 2, jobs: [job]}))
    const incomplete = cli()
    assert.equal(incomplete.status, 1)
    assert.match(incomplete.stderr, /Incomplete/)
    assert.equal(incomplete.stdout, '')
  } finally {
    rmSync(scratch, {recursive: true, force: true})
  }
})
