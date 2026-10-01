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
const run = {id: 1, head_sha: 'abc', event: 'push', run_attempt: 2, status: 'completed', conclusion: 'failure',
  created_at: '2026-10-01T00:00:00Z', run_started_at: '2026-10-01T00:00:02Z'}
const job = {id: 10, name: 'gate', run_id: 1, run_attempt: 2, head_sha: 'abc',
  html_url: 'https://github.com/example/repo/actions/runs/1/job/10', runner_id: 7,
  status: 'completed', conclusion: 'failure', created_at: '2026-10-01T00:00:10Z',
  started_at: '2026-10-01T00:00:15Z', completed_at: '2026-10-01T00:00:35Z',
  steps: [{number: 1, name: 'test', status: 'completed', conclusion: 'failure',
    started_at: '2026-10-01T00:00:20Z', completed_at: '2026-10-01T00:00:30Z'}]}
const report = (jobs = [job], extra = {}) => buildActionsTimingReport({run, jobs, ...extra})
const checkout = {verifiedByCaller: true, runId: 1, runAttempt: 2, jobId: 10,
  headSha: 'abc', event: 'push', sourceJobUrl: job.html_url, checkoutSha: 'abc', checkoutTree: 'tree', parents: []}

test('separates dispatch, dependency/orchestration delay, waiting and execution', () => {
  const result = report()
  assert.equal(result.run.runCreatedToAttemptStartMs, 2000)
  assert.equal(result.run.runLifetimeToLastJobMs, 35000)
  assert.equal(result.run.attemptObservedWallMs, 33000)
  assert.equal(result.jobs[0].attemptStartToJobCreatedMs, 8000)
  assert.equal(result.jobs[0].observableWaitMs, 5000)
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
    assert.equal(result.jobs[0].observableWaitMs, null)
    assert.equal(result.run.attemptObservedWallMs, null)
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
  const result = report([job], {gateReports: [{jobId: 10, checkout, report: {status: 'PASS', subject: {baseHead: 'abc', testedTree: 'tree'},
    results: [{id: 'native', passed: true, durationMs: 500, report: {results: [{id: 'suite', passed: true, durationMs: 400}]}},
      {id: 'shared', passed: true, durationMs: 0, reusedFrom: {testedTree: 'previous'}},
      {id: 'failed', passed: false, durationMs: 600, exitCode: 1}]}}]})
  assert.equal(result.gateReports[0].status, 'PASS')
  assert.deepEqual(result.gateReports[0].subject, {baseHead: 'abc', testedTree: 'tree'})
  assert.deepEqual(result.gateReports[0].slowSteps.map(step => step.executionMs), [600, 500, 400, 0])
  assert.equal(result.gateReports[0].slowSteps[0].passed, false)
  assert.deepEqual(result.gateReports[0].slowSteps[3].reusedFrom, {testedTree: 'previous'})
  assert.throws(() => report([job], {gateReports: [{jobId: 99, report: {}}]}), /unknown job/)
})

test('rerun attempt elapsed time excludes the previous attempt and idle time', () => {
  const result = buildActionsTimingReport({run: {...run, run_started_at: '2026-10-01T00:00:15Z'}, jobs: [job]})
  assert.equal(result.run.attemptObservedWallMs, 20000)
  assert.equal(result.run.runLifetimeToLastJobMs, 35000)
  assert.equal(result.jobs[0].attemptStartToJobCreatedMs, null)
  assert.match(result.caveats.join(' '), /Do not sum overlapping attempt/)
})

test('skipped bookkeeping timestamps never become job or step execution', () => {
  const result = report([{...job, conclusion: 'skipped', steps: [{...job.steps[0], conclusion: 'skipped'}]}], {dependencies: {gate: []}})
  assert.equal(result.jobs[0].observableWaitMs, null)
  assert.equal(result.jobs[0].executionMs, null)
  assert.equal(result.jobs[0].steps[0].executionMs, null)
  assert.equal(result.run.attemptObservedWallMs, null)
  assert.equal(result.lastCompletingJob, null)
  assert.equal(result.criticalExecutionPath.available, false)
})

test('rejects a stale or unpinned gate artifact and never claims evidence authentication', () => {
  for (const subject of [{baseHead: 'old'}, {testedTree: 'tree'}, undefined]) {
    assert.throws(() => report([job], {gateReports: [{jobId: 10, checkout, report: {subject, results: []}}]}), /checkout commit and tree/)
  }
  const gate = report([job], {gateReports: [{jobId: 10, checkout, report: {subject: {baseHead: 'abc', testedTree: 'tree'}, results: [{id: 'negative', durationMs: -1}]}}]}).gateReports[0]
  assert.equal(gate.provenance.checkoutRecordMatched, true)
  assert.equal(gate.provenance.checkoutEvidenceAuthenticated, false)
  assert.equal(gate.provenance.artifactJobAndAttemptVerified, false)
  assert.equal(gate.steps[0].executionMs, null)
})

test('PR REST head differs from the verified synthetic merge checkout: Platform PR 246', () => {
  // Independently read run/job REST, source checkout log and Git commit resource.
  const prRun = {...run, id: 36803894385, run_attempt: 1, event: 'pull_request', head_sha: '3722601bd1142031b965a13e07e772842f339e37'}
  const prJob = {...job, id: 110183939270, run_id: 36803894385, run_attempt: 1,
    head_sha: '3722601bd1142031b965a13e07e772842f339e37',
    html_url: 'https://github.com/Harborline-Software/harborline-platform/actions/runs/36803894385/job/110183939270'}
  const prCheckout = {verifiedByCaller: true, runId: 36803894385, runAttempt: 1, jobId: 110183939270,
    event: 'pull_request', headSha: '3722601bd1142031b965a13e07e772842f339e37', sourceJobUrl: prJob.html_url,
    checkoutSha: 'f6ee8ad943f9db1abc05477301fe7275d28f8c34', checkoutTree: '51e1b2f3217e73056dce8dbf53dceba43ecec799',
    parents: ['de97dfc12a28d75fa3e450522d7eba6b561e2126', '3722601bd1142031b965a13e07e772842f339e37']}
  const gate = {jobId: 110183939270, checkout: prCheckout, report: {status: 'PASS',
    subject: {baseHead: 'f6ee8ad943f9db1abc05477301fe7275d28f8c34', testedTree: '51e1b2f3217e73056dce8dbf53dceba43ecec799'}, results: []}}
  const build = (gateInput = gate) => buildActionsTimingReport({run: prRun, jobs: [prJob], gateReports: [gateInput]})
  assert.equal(build().gateReports[0].provenance.binding, 'pull-request-synthetic-merge')
  for (const change of [{runId: 2}, {runAttempt: 2}, {jobId: 2}, {event: 'push'}, {headSha: 'wrong'},
    {sourceJobUrl: 'wrong'}, {verifiedByCaller: false}, {parents: ['base', 'wrong-head']}]) {
    assert.throws(() => build({...gate, checkout: {...prCheckout, ...change}}))
  }
  assert.throws(() => build({...gate, checkout: undefined}), /caller-verified/)
  assert.throws(() => build({...gate, report: {...gate.report, subject: {...gate.report.subject, testedTree: 'wrong'}}}), /checkout commit and tree/)
  assert.throws(() => buildActionsTimingReport({run: {...prRun, event: 'merge_group'}, jobs: [prJob],
    gateReports: [{...gate, checkout: {...prCheckout, event: 'merge_group'}}]}), /Non-PR checkout/)
})

test('cancelled before runner assignment is time to cancellation, never execution: run 36776666123', () => {
  const cancelledRun = {...run, id: 36776666123, run_attempt: 1, conclusion: 'cancelled',
    head_sha: 'df8d298cb25c57f70d6ef6381e7a246f166ffb7c', run_started_at: '2026-09-30T21:01:26Z', created_at: '2026-09-30T21:01:26Z'}
  const cancelledJob = {...job, id: 110096189734, run_id: 36776666123, run_attempt: 1,
    head_sha: 'df8d298cb25c57f70d6ef6381e7a246f166ffb7c', conclusion: 'cancelled', runner_id: 0, runner_name: '', steps: [],
    created_at: '2026-09-30T21:01:26Z', started_at: '2026-09-30T21:01:26Z', completed_at: '2026-09-30T21:07:04Z'}
  const result = buildActionsTimingReport({run: cancelledRun, jobs: [cancelledJob], dependencies: {gate: []}})
  assert.equal(result.jobs[0].conclusion, 'cancelled')
  assert.equal(result.jobs[0].timeToCancellationMs, 338000)
  assert.equal(result.jobs[0].observableWaitMs, null)
  assert.equal(result.jobs[0].executionMs, null)
  assert.equal(result.criticalExecutionPath.available, false)
  assert.equal(result.lastCompletingJob, null)
})

test('assigned cancelled jobs retain their observed execution, including before the first step', () => {
  const result = report([{...job, conclusion: 'cancelled', runner_id: 42, steps: []}])
  assert.equal(result.jobs[0].cancelledWithoutRunner, false)
  assert.equal(result.jobs[0].timeToCancellationMs, null)
  assert.equal(result.jobs[0].observableWaitMs, 5000)
  assert.equal(result.jobs[0].executionMs, 20000)
})

test('explicit graphs cannot contradict observed execution order or claim workflow verification', () => {
  const child = {...job, id: 11, name: 'child'}
  assert.throws(() => report([job, child], {dependencies: {gate: [], child: ['gate']}}), /timestamps overlap/)
  assert.equal(report([job], {dependencies: {gate: []}}).criticalExecutionPath.dependenciesVerified, false)
  assert.equal(report([{...job, completed_at: job.created_at}]).run.attemptObservedWallMs, null)
})

test('CLI reads paginated exports, refuses incomplete pages and keeps failure outcome', () => {
  const scratch = mkdtempSync(join(tmpdir(), 'actions-timing-'))
  try {
    const runPath = join(scratch, 'run.json'), jobsPath = join(scratch, 'jobs.json')
    writeFileSync(runPath, JSON.stringify(run))
    const cli = (extra = []) => spawnSync(process.execPath, [fileURLToPath(new URL('../actions-timing-report.mjs', import.meta.url)), runPath, jobsPath, ...extra], {encoding: 'utf8'})
    writeFileSync(jobsPath, JSON.stringify([{total_count: 2, jobs: [job]}, {total_count: 2, jobs: [{...job, id: 11, name: 'other'}]}]))
    const result = cli()
    assert.equal(result.status, 0)
    assert.equal(JSON.parse(result.stdout).run.conclusion, 'failure')
    assert.equal(JSON.parse(result.stdout).jobs.length, 2)
    const gatePath = join(scratch, 'gate.json'), checkoutPath = join(scratch, 'checkout.json')
    writeFileSync(gatePath, JSON.stringify({status: 'PASS', subject: {baseHead: 'abc', testedTree: 'tree'}, results: []}))
    writeFileSync(checkoutPath, JSON.stringify(checkout))
    assert.equal(cli(['-', gatePath, '10']).status, 1)
    const withGate = cli(['-', gatePath, '10', checkoutPath])
    assert.equal(withGate.status, 0)
    assert.equal(JSON.parse(withGate.stdout).gateReports[0].provenance.checkoutRecordMatched, true)
    writeFileSync(jobsPath, JSON.stringify({total_count: 2, jobs: [job]}))
    const incomplete = cli()
    assert.equal(incomplete.status, 1)
    assert.match(incomplete.stderr, /Incomplete/)
    assert.equal(incomplete.stdout, '')
  } finally {
    rmSync(scratch, {recursive: true, force: true})
  }
})
