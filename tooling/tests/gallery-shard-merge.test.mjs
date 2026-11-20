// Ticket 098 phase 3, the sharding half. Playwright --shard splits the suite across independent
// invocations, and each writes its own json report; the gate merges them before observation.
//
// The property under test is not "does it concatenate". It is that a shard which SILENTLY DID NOT
// RUN cannot shrink the suite into a smaller, quietly passing one. observeGalleryRun reconciles the
// observed scenarios against the catalog, so a missing shard must leave the merged report short —
// which surfaces as declared-but-not-run rather than as green.
//
// This is the failure the first sharded run actually had: four shards ran and all passed, every
// shard wrote to the same results.json because an explicit `outputFile` overrides
// PLAYWRIGHT_JSON_OUTPUT_NAME, and the merged report observed ZERO tests.

import assert from 'node:assert/strict'
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { resolve } from 'node:path'
import test from 'node:test'
import {EventEmitter} from 'node:events'
import {createServer} from 'node:net'
import {spawnSync} from 'node:child_process'

import { mergeShardReports } from '../gallery-shard-merge.mjs'
import { observeGalleryRun } from '../gallery-observations.mjs'
import {
  linuxGroupHasRunningMembers, assertPortAvailable, stopServers, withServerCleanup, aggregateShardResults, collectShardReports, observeCompletedGalleryRun, parseShardAssignment, requiredGalleryStepIds,
} from '../run-gallery-gate.mjs'

const SUFFIX = ' is accessible and visually conformant'
const shard = (scenarioIds, stats = {}) => ({
  suites: [{specs: scenarioIds.map(id => ({title: `${id}${SUFFIX}`}))}],
  stats: {expected: scenarioIds.length, unexpected: 0, flaky: 0, skipped: 0, ...stats},
})

test('every shard contributes its specs and its stats', () => {
  const merged = mergeShardReports([shard(['a.one']), shard(['b.two']), shard(['c.three'])])
  assert.equal(merged.suites.length, 3)
  assert.equal(merged.stats.expected, 3)
})

test('stats sum rather than overwrite', () => {
  const merged = mergeShardReports([
    shard(['a.one'], {flaky: 2}),
    shard(['b.two'], {flaky: 3, unexpected: 1}),
  ])
  assert.equal(merged.stats.flaky, 5)
  assert.equal(merged.stats.unexpected, 1)
  assert.equal(merged.stats.expected, 2)
})

test('a missing shard leaves the merge SHORT, and reconciliation catches it', () => {
  // The whole point. null stands for a shard whose report was never written.
  const declared = ['a.one', 'b.two', 'c.three']
  const merged = mergeShardReports([shard(['a.one']), null, shard(['c.three'])])
  const observed = observeGalleryRun(merged, declared)
  assert.notEqual(observed.reconciliation, 'exact')
  assert.match(observed.reconciliation, /declared-but-not-run/)
  assert.match(observed.reconciliation, /b\.two/)
})

test('a complete merge reconciles exactly', () => {
  const declared = ['a.one', 'b.two', 'c.three']
  const merged = mergeShardReports([shard(['a.one']), shard(['b.two']), shard(['c.three'])])
  const observed = observeGalleryRun(merged, declared)
  assert.equal(observed.reconciliation, 'exact')
  assert.equal(observed.browserTests, 3)
  assert.equal(observed.scenarioBrowserTests, 3)
})

test('every shard missing is refused loudly, not reported as an empty pass', () => {
  // observeGalleryRun throws on an empty report rather than returning zero counts. That is what
  // turned the first broken sharded run red instead of letting it report a suite of no tests.
  assert.throws(() => observeGalleryRun(mergeShardReports([null, null]), ['a.one']),
    /report contains no tests/)
})

test('a failed browser subprocess still contributes its written observations', () => {
  const scratch = mkdtempSync(resolve(tmpdir(), 'hlp-failed-gallery-report-'))
  const reportPath = resolve(scratch, 'results.json')
  try {
    writeFileSync(reportPath, JSON.stringify(shard(['a.one'], {expected: 0, unexpected: 1})))
    const observed = observeCompletedGalleryRun({
      results: [{id: 'gallery-accessibility-and-parity', passed: false}],
      reportPath,
      scenarioIds: ['a.one'],
    })
    assert.equal(observed.browserTests, 1)
    assert.equal(observed.scenarioBrowserTests, 1)
    assert.equal(observed.outcomes.unexpected, 1)
  } finally {
    rmSync(scratch, { recursive: true, force: true })
  }
})

// T-349, the MATRIX half. The shards now run on separate hosted runners and reach the collector as
// uploaded artifacts, so there is a new way for one to go missing that the in-process merge above
// could not have: its artifact is simply not there. The property the ticket asks to be PROVED
// rather than assumed is that this still fails the gate, so each of the three ways a matrix shard
// can vanish gets its own case below.

const producer = (index, shards, scenarioIds, overrides = {}) => ({
  schemaVersion: 1,
  kind: 'gallery-shard',
  shard: index,
  shards,
  passed: true,
  results: requiredGalleryStepIds.map(id => ({id, passed: true, durationMs: 1})),
  failure: undefined,
  playwrightReport: shard(scenarioIds),
  ...overrides,
})

test('--shard rejects a subset run, the way --record already rejects GALLERY_GREP', () => {
  assert.deepEqual(parseShardAssignment('3/8'), {index: 3, total: 8})
  assert.equal(parseShardAssignment(undefined), undefined)
  assert.throws(() => parseShardAssignment('8'), /expects <index>\/<total>/)
  assert.throws(() => parseShardAssignment('9/8'), /out of range/)
  assert.throws(() => parseShardAssignment('0/8'), /out of range/)
})

test('an absent shard ARTIFACT is refused by name, not merged around', () => {
  // Shard 2 of 3 never uploaded. This is the failure the matrix introduces and the in-process
  // merge never had: nothing on disk to merge, and no job left to notice.
  const present = [
    producer(1, 3, ['a.one']),
    producer(3, 3, ['c.three']),
  ]
  assert.throws(() => collectShardReports(present), /incomplete: 1 of 3 missing \(shards 2\)/)
})

test('no shard artifacts at all is refused, not read as an empty pass', () => {
  assert.throws(() => collectShardReports([]), /matrix produced nothing/)
})

test('shards that disagree on the total are refused', () => {
  // A re-run that changed the matrix size, or a stale artifact from an earlier run.
  assert.throws(
    () => collectShardReports([producer(1, 2, ['a.one']), producer(2, 3, ['b.two'])]),
    /disagree on the shard total/,
  )
})

test('a shard that RAN and failed a step fails the collected gate, and is named', () => {
  const broken = producer(2, 2, ['b.two'], {
    passed: false,
    results: requiredGalleryStepIds.map(id => ({
      id,
      passed: id !== 'gallery-accessibility-and-parity',
      durationMs: 1,
      ...(id === 'gallery-accessibility-and-parity' ? {failureOutput: 'budget exceeded'} : {}),
    })),
  })
  const aggregated = aggregateShardResults(
    collectShardReports([producer(1, 2, ['a.one']), broken]),
    requiredGalleryStepIds,
  )
  assert.equal(aggregated.length, requiredGalleryStepIds.length)
  assert.deepEqual(aggregated.map(entry => entry.id), requiredGalleryStepIds)
  const suite = aggregated.find(entry => entry.id === 'gallery-accessibility-and-parity')
  assert.equal(suite.passed, false)
  assert.match(suite.failureOutput, /budget exceeded/)
  // Every other step passed on both shards, so only the real failure is red.
  assert.equal(aggregated.filter(entry => !entry.passed).length, 1)
})

test('a shard whose reporter wrote nothing leaves the merge short, and reconciliation catches it', () => {
  // The step "passed" but playwrightReport is null -- the shape a killed-after-exit-0 run would
  // take. mergeShardReports contributes nothing for it, so the specs are declared-but-not-run.
  const declared = ['a.one', 'b.two']
  const complete = collectShardReports([
    producer(1, 2, ['a.one']),
    producer(2, 2, ['b.two'], {playwrightReport: null}),
  ])
  const merged = mergeShardReports(complete.map(report => report.playwrightReport))
  const observed = observeGalleryRun(merged, declared)
  assert.match(observed.reconciliation, /declared-but-not-run/)
  assert.match(observed.reconciliation, /b\.two/)
})

test('a complete matrix aggregates to nine passed steps and reconciles exactly', () => {
  const declared = ['a.one', 'b.two', 'c.three']
  const complete = collectShardReports([
    producer(1, 3, ['a.one']),
    producer(3, 3, ['c.three']),
    producer(2, 3, ['b.two']),
  ])
  // Restored to shard order regardless of the order the artifacts were read in.
  assert.deepEqual(complete.map(report => report.shard), [1, 2, 3])
  const aggregated = aggregateShardResults(complete, requiredGalleryStepIds)
  assert.ok(aggregated.every(entry => entry.passed))
  const observed = observeGalleryRun(mergeShardReports(complete.map(report => report.playwrightReport)), declared)
  assert.equal(observed.reconciliation, 'exact')
  assert.equal(observed.scenarioBrowserTests, 3)
})

// T-1264: literals below encode the requested POSIX lifecycle, not production-derived expectations.
test('occupied gallery port names the listener without stopping it', async () => {
  const listener = createServer()
  await new Promise(resolveListen => listener.listen(0, '127.0.0.1', resolveListen))
  const port = listener.address().port
  try {
    for (const platform of ['darwin', 'linux']) {
      await assert.rejects(assertPortAvailable(port, {platform, inspect: (command, args) => {
        assert.equal(command, 'lsof')
        assert.deepEqual(args, ['-nP', `-iTCP:${port}`, '-sTCP:LISTEN'])
        return {stdout: 'COMMAND PID USER\nnode 1234 gallery'}
      }}), error => {
        assert.ok(error.message.includes(`gallery port ${port} is unavailable`))
        assert.match(error.message, /node 1234 gallery/)
        return true
      })
    }
    await assert.rejects(assertPortAvailable(port, {platform: 'linux', inspect: () => ({error: {code: 'ENOENT'}})}), /is unavailable/)
    assert.equal(listener.listening, true)
  } finally { await new Promise(resolveClose => listener.close(resolveClose)) }
  await assertPortAvailable(port)
})

const gone = () => { throw Object.assign(new Error('gone'), {code: 'ESRCH'}) }
for (const graceful of [true, false]) {
  test(`gallery cleanup waits for the whole group; graceful=${graceful}`, async () => {
    const calls = []
    let probes = 0
    await stopServers([{child: {pid: 1234, killed: true}}, {child: {}}], {
      platform: 'darwin', timeoutMs: 1,
      kill: (pid, signal) => {
        calls.push([pid, signal])
        if (signal === 0 && (++probes > 1 || graceful)) gone()
      },
    })
    assert.deepEqual(calls, graceful
      ? [[-1234, 'SIGTERM'], [-1234, 0]]
      : [[-1234, 'SIGTERM'], [-1234, 0], [-1234, 'SIGKILL'], [-1234, 0]])
  })
}
test('gallery cleanup preserves Windows tree kill and waits for exit', async () => {
  const calls = []
  await stopServers([{child: {pid: 1234}}], {platform: 'win32',
    treeKill: (command, args) => calls.push([command, args]),
    kill: (pid, signal) => { assert.equal(pid, 1234); assert.equal(signal, 0); gone() },
  })
  assert.deepEqual(calls, [['taskkill', ['/pid', '1234', '/T', '/F']]])
})
test('gallery cleanup ignores exited groups and reports unkillable groups', async () => {
  await stopServers([{child: {pid: 1234}}], {platform: 'linux', kill: gone})
  await assert.rejects(stopServers([{child: {pid: 1234}}], {
    platform: 'linux', kill: () => {}, groupRunning: () => true, timeoutMs: 1,
  }), /gallery server cleanup failed/)
})
for (const failure of [false, true]) {
  test(`gallery cleanup is awaited on work completion; failure=${failure}`, async () => {
    const host = new EventEmitter()
    const events = []
    const work = withServerCleanup(async () => {
      events.push('work')
      if (failure) throw new Error('step failed')
    }, {host, stop: async () => { await Promise.resolve(); events.push('stopped') }})
    if (failure) await assert.rejects(work, /step failed/)
    else await work
    assert.deepEqual(events, ['work', 'stopped'])
    for (const signal of ['SIGINT', 'SIGTERM', 'SIGHUP']) assert.equal(host.listenerCount(signal), 0)
  })
}
for (const [signal, code] of [['SIGINT', 130], ['SIGTERM', 143], ['SIGHUP', 129]]) {
  test(`gallery ${signal} waits for cleanup and removes handlers`, async () => {
    const host = new EventEmitter()
    const events = []
    let finish
    const exited = new Promise(resolveExit => { host.exit = exitCode => { events.push(exitCode); resolveExit() } })
    const work = withServerCleanup(() => new Promise(resolveWork => { finish = resolveWork }), {
      host, stop: async () => { await Promise.resolve(); events.push('stopped') },
    })
    host.emit(signal)
    await exited
    finish()
    await work
    assert.deepEqual(events, ['stopped', code])
    for (const name of ['SIGINT', 'SIGTERM', 'SIGHUP']) assert.equal(host.listenerCount(name), 0)
  })
}

// proc_pid_stat(5): after comm, fields 3/4/5 are state/parent PID/process group.
const procFixture = records => ({
  list: path => { assert.equal(path, '/proc'); return Object.keys(records) },
  read: path => {
    const record = records[path.split('/')[2]]
    if (record instanceof Error) throw record
    return record
  },
})
test('Linux group probe ignores zombies, unrelated groups, and vanished PIDs', () => {
  const records = {
    1234: '1234 (node (server)) Z 1 1234 1234 0',
    1235: '1235 (node worker) Z 1 1234 1234 0',
    9876: '9876 (unrelated) R 1 9876 9876 0',
    9999: Object.assign(new Error('gone'), {code: 'ENOENT'}),
    self: 'not a PID',
  }
  assert.equal(linuxGroupHasRunningMembers(1234, procFixture(records)), false)
  for (const state of ['R', 'S', 'D', 'T']) {
    records[1235] = `1235 (node worker) ${state} 1 1234 1234 0`
    assert.equal(linuxGroupHasRunningMembers(1234, procFixture(records)), true)
  }
  records[1235] = Object.assign(new Error('denied'), {code: 'EACCES'})
  assert.throws(() => linuxGroupHasRunningMembers(1234, procFixture(records)), /denied/)
  records[1235] = 'unparseable stat'
  assert.throws(() => linuxGroupHasRunningMembers(1234, procFixture(records)), /stat/)
})
test('Linux cleanup accepts a zombie-only group even while kill(0) succeeds', async () => {
  const calls = []
  await stopServers([{child: {pid: 1234}}], {platform: 'linux', timeoutMs: 1,
    kill: (pid, signal) => calls.push([pid, signal]),
    groupRunning: pid => linuxGroupHasRunningMembers(pid, procFixture({
      1235: '1235 (orphan worker) Z 1 1234 1234 0',
    })),
  })
  assert.deepEqual(calls, [[-1234, 'SIGTERM'], [-1234, 0]])
})
test('Linux cleanup escalates while a member runs, then accepts its zombie', async () => {
  const calls = []
  let state = 'S'
  await stopServers([{child: {pid: 1234}}], {platform: 'linux', timeoutMs: 1,
    kill: (pid, signal) => { calls.push([pid, signal]); if (signal === 'SIGKILL') state = 'Z' },
    groupRunning: pid => linuxGroupHasRunningMembers(pid, procFixture({
      1235: `1235 (worker) ${state} 1 1234 1234 0`,
    })),
  })
  assert.deepEqual(calls, [[-1234, 'SIGTERM'], [-1234, 0], [-1234, 'SIGKILL'], [-1234, 0]])
})
test('cleanup failure after completed work still emits the gate failure report', () => {
  const directory = mkdtempSync(resolve(tmpdir(), 'gallery-cleanup-report-'))
  writeFileSync(resolve(directory, 'gallery-shard-1.json'), JSON.stringify({
    shard: 1, shards: 1,
    results: requiredGalleryStepIds.map(id => ({id, passed: true, durationMs: 1})),
    playwrightReport: shard([]),
  }))
  try {
    const script = `
      process.argv.push('--merge=' + process.argv[1])
      const {main, stopServers} = await import(${JSON.stringify(new URL('../run-gallery-gate.mjs', import.meta.url).href)})
      await main({stop: () => stopServers([{child: {pid: 1234}}], {
        platform: 'linux', timeoutMs: 1, kill: () => {},
        groupRunning: () => { throw new Error('injected process probe failure') },
      })})
    `
    const result = spawnSync(process.execPath, ['--input-type=module', '-e', script, directory], {encoding: 'utf8'})
    assert.equal(result.status, 1)
    const report = JSON.parse(result.stdout)
    assert.equal(report.status, 'FAIL')
    assert.match(report.failure, /gallery server cleanup failed/)
    assert.equal(report.results.length, 9)
    assert.ok(report.results.every(entry => entry.passed), 'work completed before the injected cleanup failure')
  } finally { rmSync(directory, {recursive: true, force: true}) }
})
